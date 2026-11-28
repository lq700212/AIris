using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AIris.WPF.Services;
using AIris.Core.Detect;
using AIris.Core.Engine;
using AIris.Core.Model;

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

        public MonitorViewModel()
        {
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
        /// <summary>引擎提示行（"Mock 演示引擎"/"未接入，回落 Mock"）。</summary>
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

        /// <summary>引擎提示行：Mock 模式明示演示替身；真实模式按探活结果（未接入会标回落）。</summary>
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
                : eng.Name + " 未接入 · 回落 Mock 演示";
        }

        private void OnFrame(object s, FrameEventArgs e)
        {
            var img = Imaging.ToBitmapSource(e.Image);
            var dets = e.Detections ?? new List<Detection>();
            var info = e.InfoLine;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 空场也出帧（2026-11-23 整改）：Mock 空场是透明帧，叠在舞台网格上就是
                // "空台面"的真实观感；旧版空场清帧会回"等待画面…"，让用户误以为相机断流
                // （实际相机在线、检测也在跑）。IdleText 只留给真正无帧的状态。
                FrameImage = img ?? FrameImage;
                CameraInfo = info;
                Boxes.Clear();
                foreach (var d in dets) Boxes.Add(d);
            }));
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
