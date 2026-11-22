using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AIris.WPF.Services;
using AIris.WPF.ViewModels;
using AIris.WPF.Views;

namespace AIris.WPF
{
    /// <summary>
    /// 应用启动链（对位 HJVision-WPF App.OnStartup 模式，按 AIris 实际裁剪）：
    /// 1) KxShellDefaults 宿主级壳常量 —— 必须任何窗创建之前设（库静态配置类，设后只读）
    /// 2) 全局文本清晰化 + 软件渲染（虚拟屏/老驱动防白磁贴，必须建窗前设）
    /// 3) Mock/日志注入（Kaleidoscope MockBridge + LogHelper + DeviceServiceBase.LogSink）
    /// 4) ServiceHub 装配（存储/设置/RunMonitor）+ 主题恢复 + 启用配方装载
    /// 5) 建主窗显示 → 启动监控（Mock 演示态默认启动，画面即活）
    /// 6) 主窗关闭 → Shutdown
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // —— 1. 库壳常量：必须在任何窗创建之前设（对位 HJVision 口径，字号随 C 方案 fs:14） ——
            Kaleidoscope.UI.Views.KxShellDefaults.ShadowMargin = new Thickness(0);
            Kaleidoscope.UI.Views.KxShellDefaults.UseContentClip = true;
            Kaleidoscope.UI.Views.KxShellDefaults.ImplicitStyleKeys =
                "Button=KxDialogButtonImplicit,ComboBox=KxDialogComboImplicit,TextBox=KxDialogTextBoxImplicit," +
                "PasswordBox=KxDialogPasswordImplicit,DataGrid=KxDialogGridImplicit,ListBox=KxDialogListImplicit," +
                "GroupBox=KxGroupBoxImplicit";
            Kaleidoscope.UI.Views.KxShellDefaults.FontFamily = "Microsoft YaHei UI, 微软雅黑";
            Kaleidoscope.UI.Views.KxShellDefaults.FontSize = 14;      // C 方案 --fs:14
            Kaleidoscope.UI.Views.KxShellDefaults.ShowInTaskbar = false;
            Kaleidoscope.UI.Views.KxShellDefaults.StartupLocation = WindowStartupLocation.CenterScreen;
            Kaleidoscope.UI.Views.KxShellDefaults.SysButtonWidth = 40;
            Kaleidoscope.UI.Views.KxShellDefaults.CloseFontWeight = FontWeights.Bold;

            // —— 2. 全局文本清晰化 + 软件渲染（工控虚拟屏/老驱动防白磁贴） ——
            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent,
                new RoutedEventHandler((s, arg) => TextOptions.SetTextRenderingMode((DependencyObject)s, TextRenderingMode.ClearType)));
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            DispatcherUnhandledException += OnDispatcherException;

            // —— 3. Kaleidoscope 注入 + 运行目录 —— 
            MockBridgeInjector.Install();
            LogSinkInjector.Install();
            Core.Config.AppPaths.EnsureDirs();

            // —— 4. 装配 + 主题恢复 ——
            ThemeService.AttachStore(new Core.Config.IniFile(Core.Config.AppPaths.MainIni));
            var mainVm = new MainViewModel();
            ServiceHub.Install(mainVm);

            // —— 5. 主窗 → 启动监控 ——
            var main = new MainWindow(mainVm);
            MainWindow = main;
            main.Show();

            // 启用配方自动装载并启动（Mock 演示态开机即活；无启用配方时监控台显示空态引导）
            // 先装载配方再触发初始页回调（TouchCurrent 里的演示零件下拉依赖配方 Parts）
            var enabled = ServiceHub.Recipes.GetEnabled();
            ServiceHub.Monitor.LoadRecipe(enabled);
            mainVm.TouchCurrent();   // 初始页（运行监控台）进入回调：订阅画面/工步/状态事件
            ServiceHub.Monitor.Start();

            // —— 6. 退出链：主窗关 → 释放 RunMonitor → Shutdown（OnExplicitShutdown 模式显式） ——
            main.Closed += (s, arg) =>
            {
                try { ServiceHub.Monitor.Dispose(); } catch { }
                Shutdown();
            };
        }

        /// <summary>UI 线程未捕获异常兜底：记录 + 提示，不直接崩进程（现场排障刚需）。</summary>
        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try { AppLog.Error("UI 线程未捕获异常", e.Exception); } catch { /* 日志失败不二次抛 */ }
            MessageBox.Show("发生未处理异常，详见运行日志。\r\n" + e.Exception.Message, "AI 智瞳",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
