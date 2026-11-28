using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace AIris.WPF.ViewModels
{
    /// <summary>
    /// 主窗 VM：dock 导航 + 顶栏状态 + 主题切换。
    /// 页面集合 6 个，dock 胶囊只显示 IsDockVisible 的 5 项
    /// （配方编辑器从配方中心"编辑"进入，不占 dock——对位设计稿 main 页 nav）。
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly List<PageViewModelBase> _pages;

        public MainViewModel()
        {
            _pages = new List<PageViewModelBase>
            {
                new MonitorViewModel(),
                new RecipeListViewModel(),
                new RecipeEditorViewModel(),
                new EngineViewModel(),
                new AuditViewModel(),
                new SettingsViewModel(),
            };
            // 初始页 = 运行监控台（对位设计稿默认页）
            _currentPage = _pages[0];

            ToggleThemeCommand = new RelayCommand(() => Services.ThemeService.Toggle());
            NavigateCommand = new RelayCommand(p => Go(p as string));
            ToggleMockCommand = new RelayCommand(ToggleMock);

            // 顶栏状态胶囊每秒读一次真实运行态：相机 IsConnected 可能在运行中因断流/重连变化，
            // 引擎 IsLoaded 也可能因探活/切换变化——只靠 StateChanged 事件会漏掉相机侧变化。
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => RefreshRuntimeStatus();
        }

        /// <summary>全部页面（按 dock 顺序）。</summary>
        public IReadOnlyList<PageViewModelBase> Pages { get { return _pages; } }

        /// <summary>dock 胶囊导航项（5 项：编辑器不在 dock）。</summary>
        public IEnumerable<PageViewModelBase> DockPages { get { return _pages.Where(p => p.IsDockVisible); } }

        private PageViewModelBase _currentPage;
        /// <summary>当前页（ContentControl + DataTemplate 切换）。</summary>
        public PageViewModelBase CurrentPage
        {
            get { return _currentPage; }
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(CurrentTitle));
                    OnCurrentPageChanged(_currentPage);
                }
            }
        }

        /// <summary>顶栏当前页标题（面包屑用）。</summary>
        public string CurrentTitle { get { return _currentPage.Title; } }

        /// <summary>dock 导航（参数 = 页面 Id）。</summary>
        public ICommand NavigateCommand { get; }

        /// <summary>顶栏明暗主题切换。</summary>
        public ICommand ToggleThemeCommand { get; }

        /// <summary>顶栏「Mock 数据」全局开关（相机 + 引擎一起切演示数据）。</summary>
        public ICommand ToggleMockCommand { get; }

        private readonly DispatcherTimer _statusTimer;

        private bool _mockMode;
        /// <summary>当前是否 Mock 数据模式（ServiceHub.MockMode 的 UI 镜像）。</summary>
        public bool MockMode
        {
            get { return _mockMode; }
            private set
            {
                if (SetProperty(ref _mockMode, value)) OnPropertyChanged(nameof(MockButtonText));
            }
        }

        /// <summary>顶栏 Mock 按钮文案：开启时带"· 开"，一眼看出当前是演示数据。</summary>
        public string MockButtonText { get { return _mockMode ? "Mock 数据 · 开" : "Mock 数据"; } }

        private string _engineIndicator = "引擎 —";
        /// <summary>顶栏引擎态胶囊文本：在线 / 检测暂停 / Mock 演示 / 未接入。
        /// 不能写死"引擎 在线"——切到未接入的引擎后，顶栏会与引擎页状态自相矛盾。</summary>
        public string EngineIndicator
        {
            get { return _engineIndicator; }
            private set { SetProperty(ref _engineIndicator, value); }
        }

        private string _engineStatus = "off";
        /// <summary>顶栏引擎圆点状态：ok=绿 / warn=橙（Mock 或回落）/ off=红（未接入）。
        /// XAML 按此上色（不写死颜色，颜色键见 Theme*）。</summary>
        public string EngineStatus
        {
            get { return _engineStatus; }
            private set { SetProperty(ref _engineStatus, value); }
        }

        private string _cameraIndicator = "相机 —";
        /// <summary>顶栏相机态胶囊文本：在线 / 离线 / Mock 演示。</summary>
        public string CameraIndicator
        {
            get { return _cameraIndicator; }
            private set { SetProperty(ref _cameraIndicator, value); }
        }

        private string _cameraStatus = "off";
        /// <summary>顶栏相机圆点状态：ok=绿 / warn=橙（Mock）/ off=红（离线）。</summary>
        public string CameraStatus
        {
            get { return _cameraStatus; }
            private set { SetProperty(ref _cameraStatus, value); }
        }

        /// <summary>
        /// 运行时装配后挂订阅（ServiceHub.Install 末尾调用一次）：
        /// 顶栏引擎/相机态随运行、切换、Mock 模式刷新。构造器里 ServiceHub.Monitor 尚未装配，故订阅放在这里。
        /// </summary>
        public void AttachRuntime()
        {
            var monitor = Services.ServiceHub.Monitor;
            if (monitor == null) return;

            monitor.StateChanged += (s, e) => RefreshRuntimeStatus();
            Services.ServiceHub.MockModeChanged += OnMockModeChanged;

            MockMode = Services.ServiceHub.MockMode;
            RefreshRuntimeStatus();
            _statusTimer.Start();
        }

        /// <summary>Mock 模式切换通知（可能来自非 UI 线程）→ 调度回 UI 线程刷新按钮与胶囊。</summary>
        private void OnMockModeChanged(bool on)
        {
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                MockMode = on;
                ApplyRuntimeStatus();
                return;
            }
            app.Dispatcher.BeginInvoke(new Action(() => { MockMode = on; ApplyRuntimeStatus(); }));
        }

        /// <summary>刷新顶栏胶囊；StateChanged 可能来自检测线程 → 调度回 UI 线程。</summary>
        private void RefreshRuntimeStatus()
        {
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                ApplyRuntimeStatus();
                return;
            }
            app.Dispatcher.BeginInvoke(new System.Action(ApplyRuntimeStatus));
        }

        /// <summary>
        /// 顶栏状态唯一刷新点：只读真实运行态（MockMode / 引擎 IsLoaded / 相机 IsConnected），
        /// 不按"配置选了谁"假装在线——真实设备断流时相机胶囊必须变红，引擎探活失败必须变橙。
        /// </summary>
        private void ApplyRuntimeStatus()
        {
            var monitor = Services.ServiceHub.Monitor;
            if (monitor == null)
            {
                EngineStatus = "off"; EngineIndicator = "引擎 未接入";
                CameraStatus = "off"; CameraIndicator = "相机 未接入";
                return;
            }

            MockMode = Services.ServiceHub.MockMode;

            if (Services.ServiceHub.MockMode)
            {
                // 演示态：两个胶囊都明示 Mock（橙色），不冒充"在线"
                EngineStatus = "warn"; EngineIndicator = "引擎 Mock";
                CameraStatus = "warn"; CameraIndicator = "相机 Mock";
                return;
            }

            var eng = monitor.Engine;
            if (eng == null)
            {
                EngineStatus = "off"; EngineIndicator = "引擎 未接入";
            }
            else if (eng.IsLoaded)
            {
                EngineStatus = "ok"; EngineIndicator = "引擎 在线";
            }
            else
            {
                // 引擎未接入：检测本轮跳过（不出框、不落审计），顶栏如实显示"暂停"
                EngineStatus = "warn"; EngineIndicator = "引擎 检测暂停";
            }

            var cam = monitor.Camera;
            bool connected = cam != null && cam.IsConnected;
            CameraStatus = connected ? "ok" : "off";
            CameraIndicator = connected ? "相机 在线" : "相机 离线";
        }

        /// <summary>
        /// 顶栏「Mock 数据」点击：开启 = 相机/引擎都切演示数据；关闭 = 恢复设备页真机 + 引擎页真实卡。
        /// 二次确认写明影响范围，避免现场误以为"开着 Mock 也在做真实检测"。
        /// </summary>
        private void ToggleMock()
        {
            bool next = !Services.ServiceHub.MockMode;
            string text = next
                ? "开启后：相机切到 Mock 合成画面，检测引擎切到演示引擎。\r\n\r\n"
                + "用于没有相机 / 没有模型时查看完整流程与界面效果。\r\n"
                + "真实设备与引擎配置会保留，关闭 Mock 后自动恢复。"
                : "关闭后：相机切回设备页配置的真实相机，检测引擎切回当前引擎卡。\r\n\r\n"
                + "真实设备不可达时，顶栏状态胶囊会如实显示离线 / 回落，不会伪装在线。";
            if (!Services.ServiceHub.Host.Confirm(
                next ? "开启 Mock 数据模式" : "关闭 Mock 数据模式",
                text, next ? "开启" : "关闭", false)) return;

            Services.ServiceHub.SetMockMode(next);
            MockMode = next;
            ApplyRuntimeStatus();
        }

        /// <summary>
        /// 触发初始页的进入回调（App 装配 ServiceHub 之后调用一次）：
        /// 构造器里 _currentPage 是字段直赋，不走 OnCurrentPageChanged —— 不补这一下
        /// 初始页的事件订阅（OnEnter）永远不会挂上，监控台画面/状态全部无源。
        /// </summary>
        public void TouchCurrent()
        {
            OnCurrentPageChanged(_currentPage);
        }

        /// <summary>按 Id 找页面 VM（Host 回跳用；不存在返回 null）。</summary>
        public PageViewModelBase FindPage(string id)
        {
            return _pages.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>按 Id 切页并触发 OnEnter（页面进入刷新数据的唯一入口）。</summary>
        public void Go(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var target = _pages.FirstOrDefault(p => p.Id == id);
            if (target == null || ReferenceEquals(target, _currentPage)) return;
            CurrentPage = target;
        }

        /// <summary>直接定位页面对象（供 Host/弹窗回跳用）。</summary>
        public void Go(PageViewModelBase page)
        {
            if (page == null || ReferenceEquals(page, _currentPage)) return;
            CurrentPage = page;
        }

        /// <summary>CurrentPage 变更收口：更新 IsCurrent 高亮；旧页先 OnLeave 收尾（停预览/停订阅），新页再 OnEnter。</summary>
        private void OnCurrentPageChanged(PageViewModelBase page)
        {
            foreach (var p in _pages)
            {
                if (!ReferenceEquals(p, page) && p.IsCurrent) p.OnLeave();
                p.IsCurrent = ReferenceEquals(p, page);
            }
            page.OnEnter();
        }
    }
}
