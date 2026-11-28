using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AIris.WPF.Services;
using AIris.Core.Detect;
using AIris.Core.Engine;
using AIris.Core.Model;
using Kaleidoscope.UI.Views;

namespace AIris.WPF.ViewModels
{
    /// <summary>工步序列行（监控台序列卡 + 检测框三态归属判定）。</summary>
    public class StepRowVm : ViewModelBase
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        /// <summary>预期零件（检测框三态着色按此与框 Label 匹配归属工步）。</summary>
        public string ExpectedPart { get; set; } = "";
        public string RoiId { get; set; } = "";
        /// <summary>done / current / pending</summary>
        public string State { get; set; } = "pending";
        public string StateGlyph { get { return State == "done" ? "✓" : State == "current" ? "●" : "○"; } }
        public string StateColor { get { return State == "done" ? "ThemeOk" : State == "current" ? "ThemePrimary" : "ThemeTextMuted"; } }
    }

    /// <summary>
    /// 运行监控台 VM（设计稿 data-page="monitor"）：
    /// 订 ServiceHub.Monitor 三类事件（画面/工步/状态）转 UI 属性；演示控制条驱动 MockScene。
    /// 事件在检测线程抛出 → 统一 Dispatcher 收口（OnEnter 订阅，OnLeave 不退订——页面单例，
    /// 订阅前先减防重复）。
    /// </summary>
    public class MonitorViewModel : PageViewModelBase
    {
        public override string Id => "monitor";
        public override string Title => "运行监控";
        public override string Icon => "◉";

        private bool _hooked;

        /// <summary>页面是否处于前台（OnEnter/OnLeave 维护）。切到别的页时跳过画面推送：
        /// 相机帧仍在采集/检测，但没人看的页面没必要每帧做 15MB 像素拷贝。</summary>
        private volatile bool _pageActive;

        /// <summary>
        /// 帧呈现面（Kaleidoscope.UI 库件，V0.1.16 接入）：复用同一个 WriteableBitmap +
        /// 丢帧背压 + 合并调度。旧链路每帧 Imaging.ToBitmapSource 会新建 ~20MB 大对象
        /// （5MP 相机）+ GDI+ 24→32bpp 整帧转换 + 两次全量拷贝，GC 与 CPU 双重压力
        /// 是"视频卡顿"的根因；本类把单帧成本压到一次内存拷贝（~2ms）。
        /// 构造必须在 UI 线程（VM 由 MainViewModel 在 UI 线程创建）。
        /// MaxPixelWidth=1600：5MP→1296×972 库内下采样，UI 不再每帧缩放 5MP
        /// （软件渲染下实测显示渲染占单核约 80%，下采样后降到 ~20%）。
        /// </summary>
        private readonly KxFrameSurface _surface = new KxFrameSurface { MaxPixelWidth = 1600 };

        /// <summary>上一帧检测结果引用（RunMonitor 仅在检测更新时换新 List；
        /// 引用不变 = 框没变，跳过 Boxes 重建，静止画面零重绘开销）。</summary>
        private List<Detection> _lastDetsRef;

        /// <summary>上一帧已投递的信息行（值比较去重，避免每帧无谓 Dispatcher 收口）。</summary>
        private string _lastInfo = "";

        public MonitorViewModel()
        {
            // 帧呈现面：首帧/换分辨率时挂一次位图，之后每帧原地 WritePixels（零分配）。
            // null = Clear() 回空态（当前监控台"空场也出帧"，只在真断流时才可能用到）。
            _surface.SurfaceChanged += bmp => FrameImage = bmp;

            StartPauseCommand = new RelayCommand(StartPause);
            RecheckCommand = new RelayCommand(() => ServiceHub.Monitor.Recheck(), () => CanRecheck);
            DemoPlaceCommand = new RelayCommand(() => DemoPlace(SelectedPart));
            DemoRemoveCommand = new RelayCommand(() => ServiceHub.Monitor.DemoRemove(SelectedPart));
            DemoPlaceAllCommand = new RelayCommand(() => ServiceHub.Monitor.DemoPlaceAll());
            GoRecipeCommand = new RelayCommand(() => ServiceHub.Main.Go("recipe"));

            // Mock 数据模式由顶栏全局切换：演示控制条显隐/引擎提示要即时跟随
            ServiceHub.MockModeChanged += OnMockModeChanged;

            // 运行日志流：首屏回放环形缓冲 + 订阅新行（检测线程触发 → Dispatcher 收口；
            // VM 生命周期=App 级，订阅不退订）。条数超限移最旧（与 AppLog 环形同容量）。
            foreach (var l in AppLog.Snapshot()) Logs.Add(l);
            AppLog.LineAdded += line =>
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Logs.Add(line);
                    while (Logs.Count > 300) Logs.RemoveAt(0);
                }));
        }

        // —— 画面 ——
        private BitmapSource _frameImage;
        /// <summary>相机画面（Mock 合成/真机帧；Freeze 后跨线程安全）。</summary>
        public BitmapSource FrameImage
        {
            get { return _frameImage; }
            private set { SetProperty(ref _frameImage, value); }
        }

        /// <summary>叠加检测框（View Canvas 绘制；无置信度标签——AGENTS.md 铁律 11）。</summary>
        public ObservableCollection<Detection> Boxes { get; } = new ObservableCollection<Detection>();

        private string _cameraInfo = "2592 × 1944 · 8 fps";
        public string CameraInfo { get { return _cameraInfo; } private set { SetProperty(ref _cameraInfo, value); } }

        // —— 状态 ——
        private bool _isRunning;
        public bool IsRunning { get { return _isRunning; } private set { SetProperty(ref _isRunning, value); OnPropertyChanged(nameof(StartPauseText)); } }

        public string StartPauseText { get { return _isRunning ? "暂停检测" : "启动检测"; } }

        private string _statusLine = "未启用配方";
        public string StatusLine { get { return _statusLine; } private set { SetProperty(ref _statusLine, value); } }

        private int _okCount, _ngCount;
        public int OkCount { get { return _okCount; } private set { SetProperty(ref _okCount, value); } }
        public int NgCount { get { return _ngCount; } private set { SetProperty(ref _ngCount, value); } }

        private string _engineNote = "";
        /// <summary>引擎提示行（"Mock 演示引擎"/"未接入，检测暂停"）。</summary>
        public string EngineNote { get { return _engineNote; } private set { SetProperty(ref _engineNote, value); } }

        // —— 当前工步引导卡 ——
        private string _stepPos = "—";
        public string StepPos { get { return _stepPos; } private set { SetProperty(ref _stepPos, value); } }

        private string _stepName = "";
        public string StepName { get { return _stepName; } private set { SetProperty(ref _stepName, value); } }

        private string _expectLine = "";
        /// <summary>预期行（"预期：4 颗 M3 盘头螺丝出现在 ROI-B"）。</summary>
        public string ExpectLine { get { return _expectLine; } private set { SetProperty(ref _expectLine, value); } }

        private string _ruleLine = "";
        /// <summary>判定行（"IoU ≥ 0.35 × 稳定 3 帧（当前 2/3）"）。</summary>
        public string RuleLine { get { return _ruleLine; } private set { SetProperty(ref _ruleLine, value); } }

        private string _verdict = "wait";
        /// <summary>wait / ok / ng（判定胶囊与 NG 卡显隐）。</summary>
        public string Verdict { get { return _verdict; } private set { SetProperty(ref _verdict, value); OnPropertyChanged(nameof(HasNg)); OnPropertyChanged(nameof(NoNg)); } }

        public bool HasNg { get { return _verdict == "ng"; } }
        public bool NoNg { get { return _verdict != "ng"; } }

        private string _ngDetail = "";
        public string NgDetail { get { return _ngDetail; } private set { SetProperty(ref _ngDetail, value); } }

        private VerdictEventArgs _lastNg;
        public VerdictEventArgs LastNg { get { return _lastNg; } private set { SetProperty(ref _lastNg, value); } }

        // —— 工步序列 ——
        public ObservableCollection<StepRowVm> Steps { get; } = new ObservableCollection<StepRowVm>();

        // —— 运行日志流（AppLog 双出口的 UI 面板侧；环形限 300 条防长跑膨胀） ——
        public ObservableCollection<LogLine> Logs { get; } = new ObservableCollection<LogLine>();

        // —— 演示控制（仅 Mock 数据模式显示） ——

        private bool _isMockMode;
        /// <summary>当前是否全局 Mock 数据模式：演示控制条只在演示态显示
        /// （真机 + 真实引擎时驱动 MockScene 没有意义，会误导成"点一下真机画面会变"）。</summary>
        public bool IsMockMode { get { return _isMockMode; } private set { SetProperty(ref _isMockMode, value); } }

        /// <summary>演示零件选项（启用配方的 Parts）。</summary>
        public ObservableCollection<string> DemoParts { get; } = new ObservableCollection<string>();

        private void OnMockModeChanged(bool on)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                IsMockMode = on;
                RefreshEngineNote();
                return;
            }
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsMockMode = on;
                RefreshEngineNote();
            }));
        }

        private string _selectedPart;
        public string SelectedPart { get { return _selectedPart; } set { SetProperty(ref _selectedPart, value); } }

        public ICommand StartPauseCommand { get; }
        public ICommand RecheckCommand { get; }
        public ICommand DemoPlaceCommand { get; }
        public ICommand DemoRemoveCommand { get; }
        public ICommand DemoPlaceAllCommand { get; }
        public ICommand GoRecipeCommand { get; }

        private bool CanRecheck { get { return _verdict == "ng"; } }

        /// <summary>演示：放上一个零件（NG 修复/正常装配演示）。</summary>
        private void DemoPlace(string part)
        {
            if (string.IsNullOrEmpty(part)) return;
            ServiceHub.Monitor.DemoPlace(part);
        }

        private void StartPause()
        {
            if (_isRunning) ServiceHub.Monitor.Pause();
            else ServiceHub.Monitor.Start();
        }

        /// <summary>NG 详情弹窗（告警卡"NG 详情…"按钮）。</summary>
        public void OpenNgDetail()
        {
            if (_lastNg != null) ServiceHub.Host.ShowNgDetail(_lastNg, ServiceHub.Monitor.Recipe);
        }

        // —— 事件接线（OnEnter 订，页面单例不退订） ——

        public override void OnEnter()
        {
            _pageActive = true;
            var m = ServiceHub.Monitor;
            if (!_hooked)
            {
                _hooked = true;
                m.FrameReady += OnFrame;
                m.StepChanged += OnStepChanged;
                m.Verdict += OnVerdict;
                m.StateChanged += OnState;
            }
            IsMockMode = ServiceHub.MockMode;
            RefreshSteps();
            RefreshDemoParts();
            RefreshEngineNote();
        }

        /// <summary>离开监控台：停止画面推送（事件订阅保留，回页 OnEnter 即恢复）。</summary>
        public override void OnLeave()
        {
            _pageActive = false;
        }

        /// <summary>引擎提示行：Mock 模式明示演示替身；真实模式按探活结果（未接入标检测暂停）。</summary>
        private void RefreshEngineNote()
        {
            if (ServiceHub.MockMode)
            {
                EngineNote = "Mock 演示引擎 · 相机/引擎均为合成数据";
                return;
            }
            var eng = ServiceHub.Monitor == null ? null : ServiceHub.Monitor.Engine;
            EngineNote = eng == null ? ""
                : eng.IsLoaded ? eng.Name
                : eng.Name + " 未接入 · 检测暂停（不出框）";
        }

        private void OnFrame(object s, FrameEventArgs e)
        {
            // 页面不在前台：帧没人看，跳过像素拷贝（检测/审计仍在 Core 层照常跑）
            if (!_pageActive) return;

            // 画面：按源像素格式直推呈现面（后台线程同步拷贝一次，返回即可 Dispose 源帧）。
            // 注意：不能把 e.Image 的指针异步传下去——RunMonitor 在检测线程 finally 里 Dispose，
            // PushFrame 契约是"返回时像素已拷走"，所以这里同步调用是安全的唯一姿势。
            if (e.Image != null) PushBitmapFrame(e.Image);

            var dets = e.Detections;
            bool boxesChanged = !ReferenceEquals(dets, _lastDetsRef);
            if (boxesChanged) _lastDetsRef = dets;

            string info = e.InfoLine ?? "";
            bool infoChanged = !string.Equals(info, _lastInfo, StringComparison.Ordinal);
            if (infoChanged) _lastInfo = info;

            // 画面已由 surface 直刷；框/信息行都没变就完全不进 Dispatcher（零排队）
            if (!boxesChanged && !infoChanged) return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 空场也出帧（2026-11-23 整改）：Mock 空场是透明帧，叠在舞台网格上就是
                // "空台面"的真实观感；旧版空场清帧会回"等待画面…"，让用户误以为相机断流
                // （实际相机在线、检测也在跑）。IdleText 只留给真正无帧的状态。
                CameraInfo = info;
                if (!boxesChanged) return;
                Boxes.Clear();
                if (dets != null) foreach (var d in dets) Boxes.Add(d);
            }));
        }

        /// <summary>
        /// GDI Bitmap → KxFrameSurface（检测/相机线程调用）。
        /// 【性能红线】LockBits 必须按<b>源格式</b>请求：旧路径固定请求 Format32bppArgb
        /// 读 24bpp 相机裸流，GDI+ 会做整帧格式转换（5MP 实测 20~40ms/帧），是卡顿主因之一。
        /// 源格式不在三档白名单时（8bpp/索引色等罕见源）才回落 24bpp 转换，保证画面不黑。
        /// </summary>
        private void PushBitmapFrame(Bitmap bmp)
        {
            BitmapData bd = null;
            try
            {
                KxFramePixelFormat? fmt = MapPixelFormat(bmp.PixelFormat);
                PixelFormat lockFormat = fmt.HasValue ? bmp.PixelFormat : PixelFormat.Format24bppRgb;

                bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                  ImageLockMode.ReadOnly, lockFormat);
                if (!fmt.HasValue) fmt = KxFramePixelFormat.Bgr24;

                _surface.PushFrame(bd.Scan0, bmp.Width, bmp.Height, bd.Stride, fmt.Value);
            }
            catch (Exception ex)
            {
                // 丢一帧不影响检测主链路；日志节流（仅异常时写，正常帧零日志开销）
                AppLog.Warn("画面帧转换失败（已丢帧）：" + ex.Message);
            }
            finally
            {
                if (bd != null) bmp.UnlockBits(bd);
            }
        }

        /// <summary>GDI 像素格式 → 呈现面格式（对应关系与 alpha 语义见 KxFramePixelFormat 注释）。</summary>
        private static KxFramePixelFormat? MapPixelFormat(PixelFormat pf)
        {
            switch (pf)
            {
                case PixelFormat.Format24bppRgb: return KxFramePixelFormat.Bgr24;
                case PixelFormat.Format32bppArgb: return KxFramePixelFormat.Bgra32;
                case PixelFormat.Format32bppPArgb: return KxFramePixelFormat.Pbgra32;
                default: return null;
            }
        }

        private void OnStepChanged(object s, StepEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshSteps();
                var step = e.Step;
                StepPos = e.Recipe == null ? "—" : "工步 " + e.StepIndex + " / " + e.Recipe.StepCount;
                StepName = step == null ? "整检完成" : step.Name;
                if (step != null)
                {
                    string where = string.IsNullOrEmpty(step.RoiId) ? "全画面" : step.RoiId;
                    string act = step.Expectation == StepExpectation.Disappear ? "从 " + where + " 消失"
                               : step.Expectation == StepExpectation.FinalCheck ? "整检全部零件齐套"
                               : "出现在 " + where;
                    ExpectLine = "预期：" + step.ExpectedPart + " " + act;
                    RuleLine = "判定：" + WorkstepEngine.RuleSnapshot(step) + "（当前 0/" + Math.Max(1, step.Rule.StableFrames) + "）";
                }
                Verdict = "wait";
                NgDetail = "";
            }));
        }

        private void OnVerdict(object s, VerdictEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                RuleLine = "判定：" + (e.Step == null ? "" : WorkstepEngine.RuleSnapshot(e.Step)).Replace("）", "（当前 " + e.StableNow + "/" + e.StableNeed + "）");
                if (e.IsFinal)
                {
                    if (e.Pass)
                    {
                        Verdict = "ok";
                        NgDetail = "";
                        // OK 瞬间 StepChanged 会紧跟刷新序列；此处只更新判定胶囊
                    }
                    else
                    {
                        Verdict = "ng";
                        NgDetail = e.Step == null ? e.Detail : "工步 " + e.StepIndex + " " + e.Detail + " · 声光报警已触发";
                        LastNg = e;
                        OnPropertyChanged(nameof(CanRecheck));
                    }
                }
            }));
        }

        private void OnState(object s, RunStateEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsRunning = e.Running;
                StatusLine = e.StatusLine;
                OkCount = e.OkCount;
                NgCount = e.NgCount;
                IsMockMode = ServiceHub.MockMode;
                RefreshEngineNote();
            }));
        }

        /// <summary>刷新序列卡（done/current/pending 三态）。</summary>
        private void RefreshSteps()
        {
            var recipe = ServiceHub.Monitor.Recipe;
            Steps.Clear();
            if (recipe == null || recipe.Steps == null) return;
            int cur = ServiceHub.Monitor.Steps.CurrentIndex;
            for (int i = 0; i < recipe.Steps.Count; i++)
            {
                Steps.Add(new StepRowVm
                {
                    Index = i + 1,
                    Name = recipe.Steps[i].Name,
                    ExpectedPart = recipe.Steps[i].ExpectedPart ?? "",
                    RoiId = string.IsNullOrEmpty(recipe.Steps[i].RoiId) ? "全画面" : recipe.Steps[i].RoiId,
                    State = (i + 1) < cur ? "done" : (i + 1) == cur ? "current" : "pending"
                });
            }
            // 无运行时也把首步信息填上（引导卡初始态）
            if (cur > 0 && cur <= recipe.Steps.Count)
            {
                var step = recipe.Steps[cur - 1];
                StepPos = "工步 " + cur + " / " + recipe.StepCount;
                StepName = step.Name;
            }
        }

        /// <summary>刷新演示零件选项（启用配方 Parts）。</summary>
        private void RefreshDemoParts()
        {
            DemoParts.Clear();
            var recipe = ServiceHub.Monitor.Recipe;
            if (recipe != null && recipe.Parts != null)
                foreach (var p in recipe.Parts) DemoParts.Add(p);
            if (string.IsNullOrEmpty(SelectedPart) && DemoParts.Count > 0) SelectedPart = DemoParts[0];
        }
    }
}
