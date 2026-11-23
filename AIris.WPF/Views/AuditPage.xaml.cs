using System.Windows;
using System.Windows.Controls;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>审计追溯页 glue：行"⋯"打开存档详情（走 Host 门面）。</summary>
    public partial class AuditPage : UserControl
    {
        private AuditViewModel _vm;

        public AuditPage()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => { _vm = DataContext as AuditViewModel; };
        }

        /// <summary>存档详情按钮（DataTemplate 里拿行 DataContext）。
        /// 2026-11-23 修复：改为点击时从自身 DataContext 取 VM——旧版依赖 DataContextChanged
        /// 缓存的 _vm，页面由 DataTemplate 重建时缓存可能为空，表现为点「⋯」完全无反应。</summary>
        private void Shot_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var row = btn == null ? null : btn.DataContext as AuditRowVm;
            var vm = DataContext as AuditViewModel;
            if (vm != null && row != null) vm.OpenShot(row);
        }
    }
}
