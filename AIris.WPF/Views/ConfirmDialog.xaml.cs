using System.Windows;
using System.Windows.Media;

namespace AIris.WPF.Views
{
    /// <summary>确认弹窗 glue：三行内容注入 + danger 红钮 + hideCancel 信息态。</summary>
    public partial class ConfirmDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        public bool Result { get; private set; }

        public ConfirmDialog(string title, string text, string confirmText, bool danger, bool hideCancel = false)
        {
            InitializeComponent();
            TitleText.Text = title;
            BodyText.Text = text;
            ConfirmBtn.Content = confirmText;
            Title = title;
            if (danger) ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(0xCF, 0x43, 0x43));
            if (hideCancel) CancelBtn.Visibility = Visibility.Collapsed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) { Result = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
