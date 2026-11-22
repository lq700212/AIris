using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

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

        private string _engineIndicator = "引擎 —";
        /// <summary>顶栏引擎态胶囊文本：在线 / 回落 Mock / 未接入。
        /// 不能写死"引擎 在线"——切到未接入的引擎后，顶栏会与引擎页状态自相矛盾。</summary>
        public string EngineIndicator
        {
            get { return _engineIndicator; }
            private set { SetProperty(ref _engineIndicator, value); }
        }

        /// <summary>
        /// 运行时装配后挂订阅（ServiceHub.Install 末尾调用一次）：
        /// 顶栏引擎态随运行/切换刷新。构造器里 ServiceHub.Monitor 尚未装配，故订阅放在这里。
        /// </summary>
        public void AttachRuntime()
        {
            var monitor = Services.ServiceHub.Monitor;
            if (monitor == null) return;
            monitor.StateChanged += (s, e) => RefreshEngineIndicator();
            RefreshEngineIndicator();
        }

        /// <summary>按当前引擎实例刷新顶栏胶囊；StateChanged 可能来自检测线程 → 调度回 UI 线程。</summary>
        private void RefreshEngineIndicator()
        {
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
            {
                ApplyEngineIndicator();
                return;
            }
            app.Dispatcher.BeginInvoke(new System.Action(ApplyEngineIndicator));
        }

        private void ApplyEngineIndicator()
        {
            var monitor = Services.ServiceHub.Monitor;
            var eng = monitor == null ? null : monitor.Engine;
            EngineIndicator = eng == null ? "引擎 未接入"
                : eng.IsLoaded ? "引擎 在线"
                : "引擎 回落 Mock";
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

        /// <summary>CurrentPage 变更收口：更新 IsCurrent 高亮并触发 OnEnter。</summary>
        private void OnCurrentPageChanged(PageViewModelBase page)
        {
            foreach (var p in _pages) p.IsCurrent = ReferenceEquals(p, page);
            page.OnEnter();
        }
    }
}
