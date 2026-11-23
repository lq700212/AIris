using System.Windows;

namespace AIris.WPF.Views
{
    /// <summary>确认弹窗 glue：三行内容注入 + danger 红钮 + hideCancel 信息态。</summary>
    public partial class ConfirmDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        public ConfirmDialog(string title, string text, string confirmText, bool danger, bool hideCancel = false)
        {
            InitializeComponent();
            BodyText.Text = text;
            ConfirmBtn.Content = confirmText;
            Title = title;   // 标题只在标题条显示一处（内容区不再重复大标题）
            if (danger) ConfirmBtn.Style = (Style)FindResource("AiBtnDanger");
            if (hideCancel) CancelBtn.Visibility = Visibility.Collapsed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            // DialogResult（不是自定义属性！）：ShowDialog() 的返回值只认它——旧版只设
            // 自定义 Result 属性，调用方 `ShowDialog() == true` 恒 false，引擎切换/删除
            // 确认全部静默失效（2026-11-22 实踩："切换引擎没反应"的根因）
            DialogResult = true;
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
