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

        /// <summary>存档详情按钮（DataTemplate 里拿行 DataContext）。</summary>
        private void Shot_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var row = btn?.DataContext as AuditRowVm;
            if (_vm != null && row != null) _vm.OpenShot(row);
        }
    }
}
