using System;
using System.Collections.Generic;
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
    }

    /// <summary>运行状态事件（监控台状态区/顶栏灯）。</summary>
    public class RunStateEventArgs : EventArgs
    {
        public bool Running { get; set; }
        /// <summary>一句话状态（"运行中 · 单帧 430 ms · 批处理 61 ms/帧" / "引擎未接入，当前 Mock 演示"）。</summary>
        public string StatusLine { get; set; } = "";
        /// <summary>今日 OK / NG 计数（监控台底栏）。</summary>
        public int OkCount { get; set; }
        public int NgCount { get; set; }
    }

    /// <summary>
    /// 运行编排（调研报告 §6 架构的"采集层+检测服务+规则判定+工步引擎"粘合层，AIris Core 唯一运行态）：
    ///  相机线程: FrameArrived → 换 latestFrame + 唤醒
    ///  检测线程: 取 latestFrame → DetectAsync(prompts) → 失败降级（HTTP 引擎未接入回落 Mock 出框）
    ///           → JudgeEngine 判定 → WorkstepEngine.Feed（稳定帧累计）→ 最终结论落审计 + 截图
    /// 演示控制（无相机无模型跑通全流程）：Place/Remove 零件驱动 MockScene，Recheck 重检当前步。
    /// 线程约定：所有事件在检测线程抛出，UI 订阅方自行 Dispatcher 调度。
    /// </summary>
    public class RunMonitor : IDisposable
    {
        private readonly ICameraSource _cam;
        private IDetectionEngine _engine;
        private readonly WorkstepEngine _steps = new WorkstepEngine();
        private readonly IAuditStore _audit;

        // —— 检测线程基建 ——
        private Thread _detectThread;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly object _frameSync = new object();
        private CameraFrame _latest;            // 最新帧（替换式）
        private volatile bool _stop;

        // —— 运行态 ——
        private volatile bool _running;
        private Recipe _recipe;
        private int _okCount, _ngCount;
        private DateTime _lastDetect = DateTime.MinValue;
        private string _lastStatus = "";

        /// <summary>检测节拍（对位单帧 430ms 量级；Mock 引擎极快，节拍由相机帧率自然约束）。</summary>
        private static readonly int DetectIntervalMs = 300;

        public RunMonitor(ICameraSource cam, IDetectionEngine engine, IAuditStore audit)
        {
            _cam = cam;
            _engine = engine;
            _audit = audit;
            _cam.FrameArrived += OnFrame;
            _cam.Log += m => RaiseLog("[相机] " + m);
            _steps.StepChanged += (s, e) =>
            {
                var h = StepChanged; if (h != null) h(this, e);
            };
            _steps.Verdict += OnStepVerdict;
            _steps.AllDone += (s, e) =>
            {
                _running = false;
                RaiseState("整检通过 · 全流程完成");
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
        }

        /// <summary>启动运行（相机 + 检测线程）。</summary>
        public void Start()
        {
            if (_running) return;
            _running = true;
            _stop = false;
            _cam.Start();
            _detectThread = new Thread(DetectLoop) { IsBackground = true, Name = "AIrisDetect" };
            _detectThread.Start();
            RaiseState("运行中");
        }

        /// <summary>暂停运行（停检测线程，相机保持）。</summary>
        public void Pause()
        {
            _running = false;
            RaiseState("已暂停");
        }

        /// <summary>切换引擎（引擎页确认后调用；旧引擎释放）。</summary>
        public void SwapEngine(IDetectionEngine next, EngineConfig cfg)
        {
            var old = _engine;
            _engine = next;
            if (old != null && !ReferenceEquals(old, next)) { try { old.Dispose(); } catch { } }
            next.Log += m => RaiseLog("[引擎] " + m);   // 新实例专属订阅，不与旧实例串线（接口事件只能 +=）
            _engine.Load(cfg);
            RaiseState("引擎：" + _engine.Name + (_engine.IsLoaded ? "（已就绪）" : "（未接入，检测回落 Mock 演示）"));
        }

        public IDetectionEngine Engine { get { return _engine; } }
        public WorkstepEngine Steps { get { return _steps; } }
        public Recipe Recipe { get { return _recipe; } }

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

        // —— 检测主循环 ——

        private void OnFrame(CameraFrame frame)
        {
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
                    // UI 刷画面（带上一轮框叠加；框为空时只刷画面）
                    RaiseFrame(frame.Image, _lastDetections, BuildInfoLine());

                    // 未运行 或 无配方：只刷画面，不判定
                    if (!_running || _recipe == null) continue;

                    // 检测节拍
                    if ((DateTime.Now - _lastDetect).TotalMilliseconds < DetectIntervalMs) continue;
                    _lastDetect = DateTime.Now;

                    var step = _steps.CurrentStep;
                    if (step == null) continue;

                    // —— 检测：真实引擎；失败/未接入 → 回落 Mock（演示不中断，调研报告"引擎可插拔"） ——
                    var prompts = step.Expectation == StepExpectation.FinalCheck
                        ? new List<string>(_recipe.Parts)
                        : new List<string> { step.ExpectedPart };
                    var result = _engine.DetectAsync(frame.Image, prompts, CancellationToken.None)
                                        .GetAwaiter().GetResult();
                    List<Detection> dets;
                    string engineName;
                    if (result != null && result.Ok)
                    {
                        dets = result.Detections;
                        engineName = _engine.Name;
                    }
                    else
                    {
                        // 降级：HTTP 引擎不可达 → Mock 出框（仅在 Mock 引擎未激活时走这里）
                        var fallback = _mockEngine ?? (_mockEngine = new MockDetectionEngine());
                        var mr = fallback.DetectAsync(frame.Image, prompts, CancellationToken.None).GetAwaiter().GetResult();
                        dets = mr.Detections;
                        engineName = _engine.Name + "→Mock";
                    }
                    _lastDetections = dets;

                    // —— 判定（几何规则，无状态） ——
                    var roi = FindRoi(step.RoiId);
                    var outcome = JudgeEngine.Judge(step, roi, dets, _recipe.Parts);
                    _lastOutcome = outcome;

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

        private List<Detection> _lastDetections = new List<Detection>();
        private JudgeOutcome _lastOutcome;
        private MockDetectionEngine _mockEngine;    // 降级回落专用（懒建）
        private CameraFrame _liveFrame;             // 本轮参与判定的帧（Verdict 同步回调里截存档用）

        private ROIRect FindRoi(string roiId)
        {
            if (_recipe == null || string.IsNullOrEmpty(roiId) || _recipe.Rois == null) return null;
            foreach (var r in _recipe.Rois) if (string.Equals(r.Id, roiId, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        private string BuildInfoLine()
        {
            string baseLine = "2592 × 1944 · 8 fps";
            if (_lastOutcome != null && Engine != null)
                baseLine += " · " + Engine.Name + " " + Engine.Stats.LastElapsedMs + " ms";
            return baseLine;
        }

        /// <summary>步最终结论：落审计 + 截图 + NG 计数（订阅自 WorkstepEngine.Verdict 的收口）。</summary>
        private void OnStepVerdict(object sender, VerdictEventArgs e)
        {
            var h = Verdict;
            if (h != null) h(this, e);
            if (!e.IsFinal) return;

            if (e.Pass) _okCount++; else _ngCount++;

            // —— 截图存档：判定瞬间的画面（质量追溯链，调研报告 §6.2 第 4 条） ——
            // _liveFrame 是本步判定用的活帧（Feed 是同步回调，此刻尚未 Dispose）
            string shotFile = "";
            try
            {
                Bitmap shot = null;
                var lf = _liveFrame;
                if (lf != null && lf.Image != null) shot = (Bitmap)lf.Image.Clone();
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
                EngineName = Engine == null ? "" : Engine.Name
            });

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

        // —— 事件抛出 ——

        private void RaiseFrame(Bitmap img, List<Detection> dets, string info)
        {
            var h = FrameReady;
            if (h != null) h(this, new FrameEventArgs { Image = img, Detections = dets ?? new List<Detection>(), InfoLine = info });
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
            if (_engine != null) _engine.Dispose();
            lock (_frameSync) { if (_latest != null) { _latest.Dispose(); _latest = null; } }
            _wake.Dispose();
        }
    }
}
