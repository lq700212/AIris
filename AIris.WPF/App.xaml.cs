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
    /// 2) 全局文本清晰化 + 渲染策略（V0.1.16：默认硬件渲染，远程串流才切软件）
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
            // ShadowMargin=14（库 CureLine 缺省口径，2026-11-22 从 0 改回）：0 时弹窗卡片
            // 贴满窗矩形，Fx.WinShadow 投影被窗裁得只剩四角圆弧外月牙的灰晕——屏摄实测
            // 弹窗四角"圆角没处理干净"的脏边即此；14 留白让浮层投影完整绘制，圆角干净。
            Kaleidoscope.UI.Views.KxShellDefaults.ShadowMargin = new Thickness(14);
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

            // —— 2. 全局文本清晰化 + 渲染策略（必须建窗前设） ——
            EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent,
                new RoutedEventHandler((s, arg) => TextOptions.SetTextRenderingMode((DependencyObject)s, TextRenderingMode.ClearType)));
            DispatcherUnhandledException += OnDispatcherException;

            // —— 3. Kaleidoscope 注入 + 运行目录 —— 
            MockBridgeInjector.Install();
            LogSinkInjector.Install();
            Core.Config.AppPaths.EnsureDirs();

            // —— 3.0 后台执行门面（KxBackground）注入 ——
            // 库侧所有 fire-and-forget 任务的异常统一进 AppLog，杜绝"后台静默失败"；
            // UI 封送统一走 Dispatcher，库不依赖 WPF（可跨框架复用）。
            Kaleidoscope.Threading.KxBackground.ErrorSink =
                (name, ex) => AppLog.Error("后台任务失败：" + name, ex);
            Kaleidoscope.Threading.KxBackground.UiPost =
                action => Dispatcher.BeginInvoke(action);

            // —— 3.1 渲染策略（V0.1.16 视频卡顿整改）——
            // 旧版无条件 RenderMode.SoftwareOnly：相机 5MP 画面 8fps 的缩放/合成全压 CPU，
            // 叠加主窗分层透明（又是整窗软件渲染）→ 单核实测 63%，画面明显卡顿。
            // 现按 Kaleidoscope.UI 库 KxRenderPolicy 决策（YoloDetector v2.10.3 远程双指针
            // 方案的库化沉淀）：默认走 GPU 硬件渲染；仅检测到 UU远程/向日葵/ToDesk 等
            // 远程控制软件时才切软件渲染（避免远程画面双指针）。
            // INI [App] ForceSoftwareRendering：留空=自动；1/true=强制软件；0/false=强制硬件。
            var renderIni = new Core.Config.IniFile(Core.Config.AppPaths.MainIni);
            string forceRaw = renderIni.Read("App", "ForceSoftwareRendering", "");
            bool? forceSoftware = string.IsNullOrWhiteSpace(forceRaw)
                ? (bool?)null
                : (forceRaw.Trim() == "1" || forceRaw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
            bool softwareRender = Kaleidoscope.UI.Rendering.KxRenderPolicy.Apply(forceSoftware);
            AppLog.Info("渲染模式：" + (softwareRender ? "软件渲染（CPU）" : "硬件渲染（GPU）")
                + (forceSoftware.HasValue ? " · INI 强制" : " · 自动检测")
                + (Kaleidoscope.UI.Rendering.KxRenderPolicy.MatchedRemoteProcess != null
                    ? " · 命中远程软件 " + Kaleidoscope.UI.Rendering.KxRenderPolicy.MatchedRemoteProcess
                    : ""));

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
