using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using AIris.Core.Camera;
using AIris.Core.Config;
using AIris.Core.Detect;
using AIris.Core.Judge;
using AIris.Core.Model;
using AIris.Core.Store;

namespace AIris.Core.Engine
{
    /// <summary>叠加帧事件：UI 把 Bitmap 转 BitmapSource 显示并叠加框（帧归 RunMonitor 生命周期管理，UI 不得 Dispose）。</summary>
    public class FrameEventArgs : EventArgs
    {
        public Bitmap Image { get; set; }
        /// <summary>本帧检测框（可能为空：UI 只刷画面不出框）。</summary>
        public List<Detection> Detections { get; set; }
        /// <summary>画面信息行（"2592 × 1944 · 12 fps · 推理 430 ms/帧"）。</summary>
        public string InfoLine { get; set; } = "";
        /// <summary>场景无内容（Mock 空场；相机源恒 false）。UI 侧据此回"无帧空态"
        /// （KxImageStage 空态文本+扫描动效恢复）——空场透明帧不算"有画面"。</summary>
        public bool IsEmptyScene { get; set; }
    }

    /// <summary>运行状态事件（监控台状态区/顶栏灯）。</summary>
    public class RunStateEventArgs : EventArgs
    {
        public bool Running { get; set; }
        /// <summary>一句话状态（"运行中 · 单帧 430 ms · 批处理 61 ms/帧" / "引擎不可达，检测暂停"）。</summary>
        public string StatusLine { get; set; } = "";
        /// <summary>今日 OK / NG 计数（监控台底栏）。</summary>
        public int OkCount { get; set; }
        public int NgCount { get; set; }
    }

    /// <summary>
    /// 运行编排（落地指南 §一 架构总览的"采集层+检测服务+规则判定+工步引擎"粘合层，AIris Core 唯一运行态）：
    ///  相机线程: FrameArrived → 换 latestFrame + 唤醒
    ///  检测线程: 取 latestFrame → DetectAsync(prompts) → 失败则本轮跳过判定（不出框、不落审计）
    ///           → JudgeEngine 判定 → WorkstepEngine.Feed（稳定帧累计）→ 最终结论落审计 + 截图
    /// 演示控制（无相机无模型跑通全流程）：Place/Remove 零件驱动 MockScene，Recheck 重检当前步。
    /// 线程约定：所有事件在检测线程抛出，UI 订阅方自行 Dispatcher 调度。
    /// </summary>
    public class RunMonitor : IDisposable
    {
        private ICameraSource _cam;
        private readonly object _camSync = new object();
        private IDetectionEngine _engine;
        private readonly WorkstepEngine _steps = new WorkstepEngine();
        private readonly IAuditStore _audit;

        // —— 检测线程基建 ——
        private Thread _detectThread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _frameSync = new object();
        private CameraFrame _latest;            // 最新帧（替换式）
        private volatile bool _stop;

        // —— 引擎实例生命周期（切换保护） ——
        // 检测线程可能正卡在旧引擎的 HTTP 推理请求里（最长 HttpTimeoutMs）；若切换时直接
        // Dispose 旧引擎，会撞上 "请求还在飞、client 已被释放" 的竞态。因此：
        //   _inUseEngine      = 检测线程当前正在调用的实例（无在途调用时为 null）
        //   _pendingDispose   = 切换时仍在途的旧实例，由检测线程本轮收尾时释放
        private readonly object _engineSync = new object();
        private IDetectionEngine _inUseEngine;
        private IDetectionEngine _pendingDispose;

        // —— 实际执行者（画面信息行/日志/审计要写真实出框者，不能只写"选了谁"） ——
        private string _lastExecName = "";      // 本帧实际执行引擎（不可达时为 "X（不可达）"）
        private long _lastExecMs;               // 本帧实际执行耗时（0 = 本轮跳过）

        // —— 运行态 ——
        private volatile bool _running;
        private Recipe _recipe;
        private int _okCount, _ngCount;
        private DateTime _lastDetect = DateTime.MinValue;
        private string _lastStatus = "";

        /// <summary>检测节拍（对位单帧 430ms 量级；Mock 引擎极快，节拍由相机帧率自然约束）。</summary>
        private static readonly int DetectIntervalMs = 300;

        public RunMonitor(ICameraSource cam, IDetectionEngine engine, IAuditStore audit)
            : this(cam, engine, audit, null)
        {
        }

        /// <summary>全参构造（V0.1.7：语义决策服务可注入；null = NullSemanticDecisionService 空实现）。</summary>
        public RunMonitor(ICameraSource cam, IDetectionEngine engine, IAuditStore audit, Detect.ISemanticDecisionService semantic)
        {
            _cam = cam;
            _engine = engine;
            _audit = audit;
            _semantic = semantic ?? new Detect.NullSemanticDecisionService();
            _cam.FrameArrived += OnFrame;
            _cam.FrameArrived += OnFramePreview;    // 纯预览直通（内部判 _previewOnly，非预览态零开销）
            _cam.Log += OnCamLog;
            _steps.StepChanged += (s, e) =>
            {
                var h = StepChanged; if (h != null) h(this, e);
            };
            _steps.Verdict += OnStepVerdict;
            _steps.AllDone += (s, e) =>
            {
                _running = false;
                RaiseState("整检通过 · 全流程完成");
                RaiseLog("[运行] 整检通过 · 全流程完成（今日 OK " + _okCount + " / NG " + _ngCount + "）");
                var h = AllDone; if (h != null) h(this, e);
            };
            _engine.Log += m => RaiseLog("[引擎] " + m);
        }

        // —— 对 UI 的事件 ——
        public event EventHandler<FrameEventArgs> FrameReady;
        public event EventHandler<StepEventArgs> StepChanged;
        public event EventHandler<RunStateEventArgs> StateChanged;
        public event Action<string> Log;
        public event EventHandler<StepEventArgs> AllDone;

        /// <summary>最终判定结论事件（UI 引导卡更新 + NG 告警卡；IsFinal=true 为已落档结论）。</summary>
        public event EventHandler<VerdictEventArgs> Verdict;

        // —— 公共操作 ——

        /// <summary>装载配方并复位工步（启用配方后调用；null = 停止判定只看画面）。</summary>
        public void LoadRecipe(Recipe recipe)
        {
            _recipe = recipe;
            _steps.Reset(recipe);
            _okCount = 0;
            _ngCount = 0;
            if (recipe == null) MockScene.Instance.Clear();
            RaiseState(recipe == null ? "未启用配方" : "配方就绪：" + recipe.Name + "（" + recipe.StepCount + " 步）");
            RaiseLog(recipe == null
                ? "[配方] 未启用配方（停止判定，仅看画面）"
                : "[配方] 装载启用：" + recipe.Name + " · " + recipe.StepCount + " 步 · " + recipe.Rois.Count + " ROI");
        }

        /// <summary>启动运行（相机 + 检测线程）。</summary>
        public void Start()
        {
            if (_running) return;
            _running = true;
            _stop = false;
            _previewOnly = false;   // 编辑页预览态自动退出（帧统一走相机线程直通，防双发）
            _lastInfoLine = BuildInfoLine();   // 首帧信息行（相机线程直通读，避免空串）
            _cam.Start();
            _detectThread = new Thread(DetectLoop) { IsBackground = true, Name = "AIrisDetect" };
            _detectThread.Start();
            RaiseState("运行中");
            RaiseLog("[运行] 启动检测（相机已启 + 检测线程拉起）");
        }

        /// <summary>暂停运行（停检测线程，相机保持）。</summary>
        public void Pause()
        {
            _running = false;
            RaiseState("已暂停");
            RaiseLog("[运行] 已暂停（相机保持取流）");
        }

        /// <summary>
        /// 热切换相机源（设备页保存后立即生效，不用重启程序）。
        /// 顺序：退订旧相机事件 → 停旧相机 → 换实例 → 订新事件 → 按当前运行态决定是否启动。
        /// 线程安全：_camSync 串行化切换；旧相机退订后即使有在途帧回调，也只会走已退订的旧订阅，
        /// 不会串到新相机的检测链路。旧实例 Stop+Dispose 在锁外执行（有界等待，不阻塞 UI 过久）。
        /// </summary>
        public void SwapCamera(ICameraSource next)
        {
            if (next == null) return;

            ICameraSource old;
            bool shouldRun;
            lock (_camSync)
            {
                old = _cam;
                if (ReferenceEquals(old, next)) return;

                shouldRun = _running || _previewOnly;

                if (old != null)
                {
                    old.FrameArrived -= OnFrame;
                    old.FrameArrived -= OnFramePreview;
                    old.Log -= OnCamLog;
                }

                _cam = next;
                _cam.FrameArrived += OnFrame;
                _cam.FrameArrived += OnFramePreview;
                _cam.Log += OnCamLog;
            }

            if (old != null)
            {
                try { old.Stop(); } catch { }
                try { old.Dispose(); } catch { }
            }

            if (shouldRun)
            {
                try { next.Start(); }
                catch (Exception ex) { RaiseLog("[相机] 启动失败：" + ex.Message); }
            }

            RaiseLog("[相机] 已切换 → " + next.Name + (next.IsConnected ? "（已连接）" : "（未连接）"));
        }

        /// <summary>当前装配的相机源（设备页状态点/监控台信息行读取）。</summary>
        public ICameraSource Camera { get { return _cam; } }

        /// <summary>相机日志统一出口（方法订阅，可退订——lambda 订阅退不掉，热切换会串线）。</summary>
        private void OnCamLog(string m) { RaiseLog("[相机] " + m); }

        /// <summary>
        /// 切换引擎（引擎页确认后调用；旧引擎安全释放）。
        /// 旧引擎可能正被检测线程使用（HTTP 请求在途）：此时延迟到检测线程本轮收尾再 Dispose，
        /// 不阻塞 UI 线程，也不会出现 "请求还在飞、client 已被释放" 的竞态。
        /// 这里不调用 next.Load —— 由本方法统一装载（唯一装载点，避免重复探活等 2×2s）。
        /// </summary>
        public void SwapEngine(IDetectionEngine next, EngineConfig cfg)
        {
            if (next == null) return;

            IDetectionEngine disposeNow = null;
            IDetectionEngine old;
            lock (_engineSync)
            {
                old = _engine;
                _engine = next;
                _lastExecName = "";     // 信息行立刻退回新引擎名，下一帧检测再写实际执行者
                _lastExecMs = 0;
                if (old != null && !ReferenceEquals(old, next))
                {
                    if (ReferenceEquals(old, _pendingDispose))
                    {
                        // 切回刚被延迟释放的实例：撤销延迟释放（防御性；注册表每次 new 不会命中）
                        _pendingDispose = null;
                    }
                    else if (ReferenceEquals(old, _inUseEngine))
                    {
                        // 在途：交给检测线程收尾释放
                        _pendingDispose = old;
                    }
                    else
                    {
                        disposeNow = old;   // 没有在途调用：立刻释放
                    }
                }
            }
            if (disposeNow != null) { try { disposeNow.Dispose(); } catch { } }

            // 新实例专属订阅，不与旧实例串线（接口事件只能 +=，lambda 退不掉）。
            // 同一实例不重复订阅：构造时已订阅一次，ServiceHub 随即用同一实例调 SwapEngine
            // 装载参数——无条件再挂一次会让每条引擎日志打两遍（2026-11-28 实测）。
            if (!ReferenceEquals(old, next)) next.Log += m => RaiseLog("[引擎] " + m);
            next.Load(cfg);
            RaiseState("引擎：" + _engine.Name + (_engine.IsLoaded ? "（已就绪）" : "（未接入，检测暂停）"));
            RaiseLog("[引擎] 切换 → " + next.Name + (next.IsLoaded ? "（加载成功）" : "（未接入，检测暂停）"));
        }

        /// <summary>检测线程收尾：清在途标记；切换遗留的旧引擎此刻已无调用者，安全释放。</summary>
        private void ReleaseEngineAfterUse(IDetectionEngine used)
        {
            IDetectionEngine late = null;
            lock (_engineSync)
            {
                if (ReferenceEquals(_inUseEngine, used)) _inUseEngine = null;
                // pending 若已重新成为当前引擎（极端切回场景），不能释放
                if (_pendingDispose != null && !ReferenceEquals(_pendingDispose, _engine))
                {
                    late = _pendingDispose;
                    _pendingDispose = null;
                }
            }
            if (late != null) { try { late.Dispose(); } catch { } }
        }

        public IDetectionEngine Engine { get { return _engine; } }
        public WorkstepEngine Steps { get { return _steps; } }
        public Recipe Recipe { get { return _recipe; } }

        /// <summary>最近一帧实际执行引擎（不可达时为 "Qwen3-VL-8B（不可达）"；尚未检测为空串）。
        /// UI 用它显示真实出框者——不能把"配置选了谁"当成"实际跑了谁"。</summary>
        public string LastExecEngineName { get { return _lastExecName; } }

        /// <summary>最近一帧实际执行耗时（引擎不可达/跳过判定时为 0）。</summary>
        public long LastExecElapsedMs { get { return _lastExecMs; } }

        /// <summary>审计/日志用的执行者标签：优先实际执行者，未检测过退回当前引擎名。</summary>
        private string ExecEngineLabel()
        {
            if (!string.IsNullOrEmpty(_lastExecName)) return _lastExecName;
            return Engine == null ? "" : Engine.Name;
        }

        // —— 演示控制（运行监控台控制条） ——

        /// <summary>演示：把零件放上台面（MockScene +1）。</summary>
        public bool DemoPlace(string part) { return MockScene.Instance.PlacePart(part); }

        /// <summary>演示：拿走零件（NG 模拟，如少装一颗螺丝）。</summary>
        public bool DemoRemove(string part) { return MockScene.Instance.RemovePart(part); }

        /// <summary>演示：当前场景一键摆好（按配方 Parts 全量放置——整检演示用）。</summary>
        public void DemoPlaceAll()
        {
            if (_recipe == null) return;
            foreach (var p in _recipe.Parts)
            {
                List<int[]> layout;
                if (MockScene.PartLayout.TryGetValue(p, out layout))
                {
                    for (int i = MockScene.Instance.CountOf(p); i < layout.Count; i++) MockScene.Instance.PlacePart(p);
                }
            }
        }

        /// <summary>重检当前步（NG 挂起后人工处理完点这里）。</summary>
        public void Recheck() { _steps.Goto(_steps.CurrentIndex); }

        // —— 仅预览取像（V0.1.7：配方编辑页 ROI 标画要"在实际取像上拖框"，但不应
        //    强迫用户去监控台启动检测、也不该打扰监控台运行状态） ——

        private volatile bool _previewOnly;     // 纯预览模式：相机帧不经检测线程直接透传 FrameReady

        /// <summary>
        /// 启动纯预览取像：只开相机（幂等），相机帧经 OnFramePreview 直接透传 FrameReady
        /// （不走检测线程/判定，检测线程未起也出帧）。与 Start() 互不覆盖：检测运行中
        /// 调用本方法是无害空操作（帧本来就经 DetectLoop 透传，画面同源）。
        /// </summary>
        public void StartPreview()
        {
            if (_running) { _previewOnly = false; return; }   // 检测已在跑：帧走 DetectLoop 透传，别双发
            _previewOnly = true;
            _cam.Start();
            RaiseLog("[预览] 编辑页取像开启（仅预览，不做检测判定）");
        }

        /// <summary>停止纯预览取像：退出预览模式；检测未运行时顺手停相机（不留空转线程）。</summary>
        public void StopPreview()
        {
            _previewOnly = false;
            if (!_running)
            {
                _cam.Stop();
                lock (_frameSync)   // 清掉预览期积压帧，防下次启动判定用旧帧
                {
                    var old = _latest;
                    _latest = null;
                    if (old != null) old.Dispose();
                }
            }
            RaiseLog("[预览] 编辑页取像关闭");
        }

        /// <summary>纯预览帧直通：相机线程回调里直接透传 UI（8fps 低频，无需检测线程节拍）。
        /// 与 OnFrame（喂检测线程）分流：非预览态立即返回零开销；预览态帧不进 _latest
        /// 队列（检测线程即使误启也无帧可判，双保险）。</summary>
        private void OnFramePreview(CameraFrame frame)
        {
            if (!_previewOnly) return;      // 非预览态：OnFrame 已处理，这里零开销返回
            var h = FrameReady;
            if (h != null) h(this, new FrameEventArgs { Image = frame.Image, IsEmptyScene = frame.IsEmptyScene, Detections = new List<Detection>() });
            frame.Dispose();    // 预览直通：UI 侧 ToBitmapSource 已拷贝，帧即刻回收
        }

        // —— 检测主循环 ——

        private void OnFrame(CameraFrame frame)
        {
            if (_previewOnly) return;   // 纯预览态：帧走 OnFramePreview 直通，不进检测队列

            // —— 实测分辨率/帧率（信息行显示真实值，不写死） ——
            // 旧版信息行硬编码 "2592 × 1944 · 8 fps"，与实际流（2304×1296 ≈ 23fps）不符，
            // 现场据此判断"卡不卡/延时大不大"会得出错误结论。相机线程写、检测线程读，int 赋值原子。
            if (frame.Image != null)
            {
                _frameW = frame.Image.Width;
                _frameH = frame.Image.Height;
            }
            _fpsWindowCount++;
            long fpsNow = Stopwatch.GetTimestamp();
            double fpsElapsedMs = (fpsNow - _fpsWindowStart) * 1000.0 / Stopwatch.Frequency;
            if (fpsElapsedMs >= 1000)
            {
                // ×10 存整数（volatile 友好），显示时再除 10
                _fpsTimes10 = (int)Math.Round(_fpsWindowCount * 10000.0 / fpsElapsedMs);
                _fpsWindowStart = fpsNow;
                _fpsWindowCount = 0;
            }

            // 画面直通 UI（2026-11-28 卡顿根因修复）：
            //   旧实现把画面刷新放在 DetectLoop 里，节奏是"刷一帧 → 同步等检测 → 再刷下一帧"，
            //   检测慢多少画面就卡多少——真实引擎 HTTP 超时 6s 时画面 6s 才刷新一帧，
            //   相机 8fps 的帧全被 _latest 覆盖丢弃（用户看到的就是"卡"）。
            //   现在相机线程每帧直通 FrameReady：UI 侧 PushBitmapFrame 在调用线程同步拷像素
            //   （KxFrameSurface 内部缓冲，不碰 WPF/UI 线程）后立即返回，画面帧率只取决于相机；
            //   检测结果/信息行用缓存引用传递，引用不变时 UI 侧去重，不产生额外 Dispatcher 排队。
            //   帧随后进 _latest 供检测线程使用，生命周期不变（检测线程用完 Dispose）。
            RaiseFrame(frame.Image, _lastDetections, _lastInfoLine, frame.IsEmptyScene);

            // 相机线程只做"换帧 + 唤醒"，慢活全在检测线程（相机线程零阻塞）
            lock (_frameSync)
            {
                var old = _latest;
                _latest = frame;
                if (old != null) old.Dispose();
            }
            _wake.Set();
        }

        private void DetectLoop()
        {
            while (!_stop)
            {
                _wake.WaitOne(500);
                if (_stop) break;

                CameraFrame frame;
                lock (_frameSync) { frame = _latest; _latest = null; }
                if (frame == null) continue;

                try
                {
                    _liveFrame = frame;
                    // 画面已由相机线程在 OnFrame 里直通（帧率与检测解耦：检测慢不拖画面）；
                    // 本循环只做判定。未运行 或 无配方：直接跳过检测。
                    if (!_running || _recipe == null) continue;

                    // 检测节拍
                    if ((DateTime.Now - _lastDetect).TotalMilliseconds < DetectIntervalMs) continue;
                    _lastDetect = DateTime.Now;

                    var step = _steps.CurrentStep;
                    if (step == null) continue;

                    // —— 检测：真实引擎；失败 → 本轮跳过判定（不出假框、不落审计，见下方 else 分支注释） ——
                    // 取引擎引用 + 标记在途在同一把锁内完成：切换引擎时据此判断能否立刻 Dispose 旧实例。
                    var prompts = step.Expectation == StepExpectation.FinalCheck
                        ? new List<string>(_recipe.Parts)
                        : new List<string> { step.ExpectedPart };
                    IDetectionEngine exec;
                    lock (_engineSync) { exec = _engine; _inUseEngine = exec; }
                    DetectResult result;
                    try
                    {
                        result = exec.DetectAsync(frame.Image, prompts, CancellationToken.None)
                                    .GetAwaiter().GetResult();
                    }
                    finally
                    {
                        ReleaseEngineAfterUse(exec);    // 本轮收尾：放行"切换时在途"的旧引擎释放
                    }
                    List<Detection> dets;
                    if (result != null && result.Ok)
                    {
                        dets = result.Detections;
                        _lastExecName = exec.Name;
                        _lastExecMs = result.ElapsedMs;
                    }
                    else
                    {
                        // 引擎不可达：本轮跳过判定（不推进工步、不落审计、不出框）。
                        //
                        // 为什么不再回落 Mock 出框（2026-11-28 修正）：
                        //   MockDetectionEngine 按 MockScene 的假布局出框，画在真实相机画面上位置全错；
                        //   更严重的是它会继续参与 JudgeEngine 判定并写进审计——等于用假数据判定 OK/NG，
                        //   违反「几何判定必须确定性、可审计」红线（AGENTS.md 铁律 12）。
                        //   Mock 演示链路由 Mock 引擎自身承担（MockMode ON 时引擎就是 Mock，不走本分支）。
                        _lastDetections = EmptyDetections;
                        _lastExecName = exec.Name + "（不可达）";
                        _lastExecMs = 0;
                        _lastInfoLine = BuildInfoLine();
                        continue;   // finally 里照常 Dispose 本帧
                    }
                    _lastDetections = dets;

                    // —— 判定（几何规则，无状态） ——
                    var roi = FindRoi(step.RoiId);
                    var outcome = JudgeEngine.Judge(step, roi, dets, _recipe.Parts);
                    _lastOutcome = outcome;

                    // 信息行缓存：相机线程在 OnFrame 里直通读它（检测线程写、volatile 保可见性），
                    // 放在判定之后更新，保证下一帧画面带的是"本轮执行者 + 耗时"完整信息。
                    _lastInfoLine = BuildInfoLine();

                    // —— 稳定帧累计与推进（最终结论落审计） ——
                    _steps.Feed(outcome);
                }
                catch (Exception ex)
                {
                    RaiseLog("[检测] 异常：" + ex.Message);
                }
                finally
                {
                    frame.Dispose();    // 帧生命周期收口：检测线程用完即弃
                }
            }
        }

        /// <summary>最近一轮检测结果（检测线程整体替换、相机线程读给 UI 直通）。
        /// 每次都是新 List（旧 List 不再修改），volatile 保证跨线程可见性；
        /// UI 侧按引用去重——引用没变就不投递 Dispatcher，不产生每帧排队。</summary>
        private volatile List<Detection> _lastDetections = new List<Detection>();

        /// <summary>最近一轮信息行缓存（检测线程更新、相机线程直通读；引用传递，同上）。</summary>
        private volatile string _lastInfoLine = "";

        // —— 实测分辨率/帧率（信息行显示真实值；相机线程写，检测线程读） ——
        private volatile int _frameW;          // 最近帧宽（0=尚未出帧）
        private volatile int _frameH;          // 最近帧高
        private volatile int _fpsTimes10;      // 实测帧率 ×10（整数存，显示时 /10）
        private long _fpsWindowStart = Stopwatch.GetTimestamp();   // 统计窗口起点（仅相机线程访问）
        private int _fpsWindowCount;           // 窗口内帧数（仅相机线程访问）

        private JudgeOutcome _lastOutcome;

        /// <summary>引擎不可达时的空检测集（静态复用：引用不变，UI 侧引用去重只清一次框）。</summary>
        private static readonly List<Detection> EmptyDetections = new List<Detection>();

        private CameraFrame _liveFrame;             // 本轮参与判定的帧（Verdict 同步回调里截存档用）

        private ROIRect FindRoi(string roiId)
        {
            if (_recipe == null || string.IsNullOrEmpty(roiId) || _recipe.Rois == null) return null;
            foreach (var r in _recipe.Rois) if (string.Equals(r.Id, roiId, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        private string BuildInfoLine()
        {
            // 实测分辨率 + 帧率（不再写死 "2592 × 1944 · 8 fps"：实际流可能完全不同，
            // 错误数字会让现场对"卡不卡/延时大不大"判断失真）
            string baseLine = (_frameW > 0 ? _frameW + " × " + _frameH : "—")
                              + " · " + (_fpsTimes10 / 10.0).ToString("0.#") + " fps";
            if (_lastOutcome == null) return baseLine;

            // 实际执行者优先（不可达时显示 "Qwen3-VL-8B（不可达）"），未检测过退回当前引擎
            var name = _lastExecName;
            var ms = _lastExecMs;
            if (string.IsNullOrEmpty(name))
            {
                name = Engine == null ? "" : Engine.Name;
                ms = Engine == null ? 0 : Engine.Stats.LastElapsedMs;
            }
            if (!string.IsNullOrEmpty(name)) baseLine += " · " + name + " " + ms + " ms";
            return baseLine;
        }

        // —— 语义决策留口（V0.1.7 JEV：默认 Null 恒"未启用"零开销；启用只换装配行） ——

        /// <summary>语义决策服务（审计 NG 原因归纳增强；不进判定主链路，失败静默）。</summary>
        private readonly Detect.ISemanticDecisionService _semantic;

        /// <summary>
        /// NG 落档时问一次语义归纳（漏装/错装/位置偏移/遮挡/其他）。
        /// 未启用/失败一律返回 ""（审计主流程零依赖）；同步等待——本方法已在检测线程回调里，
        /// 云调用 2s 超时可接受（只在 NG 最终落档瞬间发生，频率极低）。
        /// </summary>
        private string TrySemanticNote(RecipeStep step, VerdictEventArgs e)
        {
            if (_semantic == null || !_semantic.Enabled) return "";
            try
            {
                string state = "工步：" + (step == null ? "?" : step.Name)
                    + "；预期零件：" + (step == null ? "?" : step.ExpectedPart)
                    + "；判定结果：" + e.Detail
                    + "；规则：" + (step == null ? "" : WorkstepEngine.RuleSnapshot(step));
                var q = new Detect.SemDecisionQuestion
                {
                    Type = Detect.SemQuestionType.Choice,
                    Instructions = "这是工业装配防错的一次 NG，最可能的原因类别是？",
                    Options = new System.Collections.Generic.Dictionary<string, string>
                    {
                        { "missing", "漏装（应装的零件没装/数量不足）" },
                        { "wrong", "错装（装了别的零件或型号不对）" },
                        { "shifted", "位置偏移（零件在但不在预期区域）" },
                        { "occluded", "遮挡（零件被手/工具/反光遮住）" },
                        { "other", "其他" },
                    },
                };
                var a = _semantic.DecideAsync(state, q, CancellationToken.None).GetAwaiter().GetResult();
                return (a != null && a.Ok && !string.IsNullOrEmpty(a.Choice)) ? a.Choice : "";
            }
            catch { return ""; }    // 云不可达等一切异常：静默跳过（增强不阻断主流程）
        }

        /// <summary>步最终结论：落审计 + 截图 + NG 计数（订阅自 WorkstepEngine.Verdict 的收口）。</summary>
        private void OnStepVerdict(object sender, VerdictEventArgs e)
        {
            var h = Verdict;
            if (h != null) h(this, e);
            if (!e.IsFinal) return;

            if (e.Pass) _okCount++; else _ngCount++;

            // —— 截图存档：判定瞬间的画面（质量追溯链，客诉举证） ——
            // _liveFrame 是本步判定用的活帧（Feed 是同步回调，此刻尚未 Dispose）
            string shotFile = "";
            try
            {
                Bitmap shot = null;
                var lf = _liveFrame;
                if (lf != null && lf.Image != null) shot = FlattenForArchive(lf.Image);
                if (shot == null) shot = ComposeShotFromDetections();
                shotFile = _audit.SaveShot(shot);
                if (shot != null) shot.Dispose();
            }
            catch (Exception ex) { RaiseLog("[审计] 截图存档失败：" + ex.Message); }

            var step = e.Step;
            _audit.Append(new AuditRecord
            {
                Time = DateTime.Now,
                Sn = "EZ" + DateTime.Now.ToString("yyMMddHHmmss"),
                RecipeName = _recipe == null ? "" : _recipe.Name,
                StepIndex = e.StepIndex,
                StepName = step == null ? "" : step.Name,
                RoiId = step == null || string.IsNullOrEmpty(step.RoiId) ? "全画面" : step.RoiId,
                Verdict = e.Pass ? "OK" : "NG",
                NgReason = e.Pass ? "" : e.Detail,
                RuleSnapshot = step == null ? "" : WorkstepEngine.RuleSnapshot(step),
                ShotFile = shotFile,
                // 实际执行者（不可达/跳过判定时不会走到本审计落档，见 DetectLoop 的 else 分支）
                EngineName = ExecEngineLabel(),
                // 语义决策备注（V0.1.7 JEV 留口）：NG 时可选归纳原因——增强链路，失败静默不阻断
                SemanticNote = e.Pass ? "" : TrySemanticNote(step, e),
            });

            // 最终判定结论进日志（运行日志面板 + 文件双出口；含实际执行引擎与存档号，追溯链入口）
            RaiseLog("[判定] 工步 " + e.StepIndex + (step == null ? "" : "·" + step.Name)
                + " → " + (e.Pass ? "OK" : "NG · " + e.Detail)
                + " · 引擎 " + ExecEngineLabel()
                + (string.IsNullOrEmpty(shotFile) ? "" : " · 存档 " + shotFile));

            RaiseState(e.Pass ? "工步 " + e.StepIndex + " OK" : "工步 " + e.StepIndex + " NG · " + e.Detail);
        }

        /// <summary>无最新帧时的占位截图（纯色底 + 框示意；极端兜底，正常路径不会走到）。</summary>
        private Bitmap ComposeShotFromDetections()
        {
            var bmp = new Bitmap(640, 480);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(240, 236, 232));
                if (_lastDetections != null)
                    foreach (var d in _lastDetections)
                        g.DrawRectangle(Pens.OrangeRed, d.X1 * 640 / 1000, d.Y1 * 480 / 1000,
                            (d.X2 - d.X1) * 640 / 1000, (d.Y2 - d.Y1) * 480 / 1000);
            }
            return bmp;
        }

        /// <summary>
        /// 存档截图专用：把活帧合成到不透明"台面"上再落盘。
        /// 为什么必须 flatten（2026-11-23 深色目检实踩）：
        /// Mock 帧是 32bppArgb 透明底（V0.1.5 口径：台面网格/扫描是 UI 舞台层，不随帧走），
        /// 直接存 PNG 的话透明区域在查看器里是透明/黑，弹窗里就成"一片空白"——
        /// 尤其"缺件 NG"记录画面上本来没零件，截图完全没有追溯价值。
        /// 合成台面（设计稿 --frame-bg #E8EDF5 + 64px 网格，与监控舞台同观感）后，
        /// 缺件 NG 也能一眼看出"台面是空的"。真实相机帧不透明，会整幅覆盖台面，无副作用。
        /// </summary>
        private static Bitmap FlattenForArchive(Bitmap frame)
        {
            var bmp = new Bitmap(frame.Width, frame.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(232, 237, 245));                       // 设计稿 --frame-bg
                using (var pen = new Pen(Color.FromArgb(26, 100, 116, 139), 1f))   // 细网格线（淡）
                {
                    for (int x = 0; x < frame.Width; x += 64) g.DrawLine(pen, x, 0, x, frame.Height);
                    for (int y = 0; y < frame.Height; y += 64) g.DrawLine(pen, 0, y, frame.Width, y);
                }
                g.DrawImageUnscaled(frame, 0, 0);   // 原尺寸叠帧：透明处透出台面，零件保留原像素
            }
            return bmp;
        }

        // —— 事件抛出 ——

        private void RaiseFrame(Bitmap img, List<Detection> dets, string info, bool emptyScene = false)
        {
            var h = FrameReady;
            if (h != null) h(this, new FrameEventArgs { Image = img, Detections = dets ?? new List<Detection>(), InfoLine = info, IsEmptyScene = emptyScene });
        }

        private void RaiseState(string status)
        {
            _lastStatus = status;
            var h = StateChanged;
            if (h != null) h(this, new RunStateEventArgs
            {
                Running = _running,
                StatusLine = _lastStatus,
                OkCount = _okCount,
                NgCount = _ngCount
            });
        }

        private void RaiseLog(string msg)
        {
            var h = Log;
            if (h != null) h(msg);
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            if (_detectThread != null) _detectThread.Join(1500);
            _running = false;
            _cam.Stop();
            _cam.Dispose();

            // 检测线程已停：此时无在途调用，当前引擎与延迟释放的旧引擎都可以安全 Dispose
            IDetectionEngine late;
            IDetectionEngine current;
            lock (_engineSync)
            {
                late = _pendingDispose;
                _pendingDispose = null;
                current = _engine;
            }
            if (late != null && !ReferenceEquals(late, current)) { try { late.Dispose(); } catch { } }
            if (current != null) { try { current.Dispose(); } catch { } }

            lock (_frameSync) { if (_latest != null) { _latest.Dispose(); _latest = null; } }
            _wake.Dispose();
        }
    }
}
