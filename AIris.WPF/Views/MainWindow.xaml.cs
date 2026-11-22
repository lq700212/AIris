using System.Windows;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 主窗 glue（对位 MVVM 纪律：View 只留 glue，其余全绑 VM）。
    /// 视觉与布局见 MainWindow.xaml 头注释（C 方案顶栏菜单壳）。
    /// 顶栏拖动/双击最大化由 ChromeBorderlessWindow 的 WindowChrome 提供（CaptionHeight=52），
    /// 顶栏交互按钮靠 shell:WindowChrome.IsHitTestVisibleInChrome=True 放行——View 层不再有 DragMove 事件。
    /// </summary>
    public partial class MainWindow : Kaleidoscope.UI.Views.ChromeBorderlessWindow
    {
        private readonly MainViewModel _vm;

        public MainWindow(MainViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            InitializeComponent();
            // 最大化 glyph 跟随 StateChanged（统一覆盖 Click 与"双击顶栏空白"系统通道两种来源）
            StateChanged += (s, e) => MaxGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "▢";

            // 【窗体圆角时序补偿】XAML 里 WindowState="Maximized" 先于根 Border 解析生效，
            // 基类 ChromeBorderlessWindow 的 StateChanged 联动（最大化圆角归零）此刻
            // RootShell 还没解析出来被跳过——Loaded 补一次归零；窗口化启动不走本支，
            // 还原/再最大化走基类 StateChanged 正常联动（见 MainWindow.xaml 头注释）。
            Loaded += (s, e) =>
            {
                if (WindowState == WindowState.Maximized)
                    RootShell.CornerRadius = new CornerRadius(0);
            };
        }

        // —— 三窗钮（最小化 / 最大化切换 / 关闭）：View 层纯窗口操作，不进 VM ——

        private void Min_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }

        /// <summary>最大化切换（glyph 由 StateChanged 统一跟随）。</summary>
        private void Max_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
