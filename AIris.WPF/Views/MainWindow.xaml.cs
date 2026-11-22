using System.Windows;
using System.Windows.Input;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 主窗 glue（对位 MVVM 纪律：View 只留三行，其余全绑 VM）。
    /// 视觉与布局见 MainWindow.xaml 头注释（C 方案 dock 壳）。
    /// </summary>
    public partial class MainWindow : Kaleidoscope.UI.Views.ChromeBorderlessWindow
    {
        private readonly MainViewModel _vm;

        public MainWindow(MainViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            InitializeComponent();
        }

        /// <summary>顶栏空白区拖动窗口（ChromeBorderlessWindow 的 WindowChrome 已覆盖系统钮区，这里只补渐变条空白区）。</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { /* 双击/最大化时 DragMove 抛异常为正常 */ } }
        }
    }
}
