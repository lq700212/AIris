namespace AIris.WPF.ViewModels
{
    /// <summary>
    /// 页面 VM 基类：dock 导航的每一页（对位设计稿 data-page）。
    /// Id 用于 INI 记忆上次停留页；Title/Icon 给 dock 胶囊与顶栏面包屑。
    /// </summary>
    public abstract class PageViewModelBase : ViewModelBase
    {
        /// <summary>页面 id（与设计稿 data-page 对齐：monitor/recipe/editor/engine/audit/settings）。</summary>
        public abstract string Id { get; }

        /// <summary>dock 胶囊显示名。</summary>
        public abstract string Title { get; }

        /// <summary>dock 胶囊图标（文本符号，与设计稿一致）。</summary>
        public abstract string Icon { get; }

        /// <summary>是否出现在底部 dock 导航（编辑器从配方中心进入，不占 dock）。</summary>
        public virtual bool IsDockVisible { get { return true; } }

        private bool _isCurrent;
        /// <summary>是否为当前页（dock 胶囊高亮；由 MainViewModel.Go 维护）。</summary>
        public bool IsCurrent
        {
            get { return _isCurrent; }
            set { SetProperty(ref _isCurrent, value); }
        }

        /// <summary>页面进入时回调（切页刷新数据，由 MainViewModel.Go 触发）。</summary>
        public virtual void OnEnter() { }
    }
}
