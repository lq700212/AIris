using System;
using AIris.Core.Camera;
using AIris.Core.Config;
using AIris.Core.Detect;
using AIris.Core.Engine;
using AIris.Core.Model;
using AIris.Core.Store;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Services
{
    /// <summary>宿主开窗/回跳门面接口（VM 只认接口不 new Window——AGENTS.md MVVM 分层铁律）。</summary>
    public interface IHost
    {
        /// <summary>新建配方向导（两步）；返回是否创建成功。</summary>
        bool ShowNewRecipe();

        /// <summary>打开配方编辑器（指定配方；null = 编辑当前启用配方）。</summary>
        void GoEditor(AIris.Core.Model.Recipe recipe);

        /// <summary>通用确认弹窗（删除/引擎切换等危险操作复用）。返回是否确认。</summary>
        bool Confirm(string title, string text, string confirmText, bool danger);

        /// <summary>NG 详情弹窗（主界面告警卡"NG 详情…"）。</summary>
        void ShowNgDetail(VerdictEventArgs verdict, AIris.Core.Model.Recipe recipe);

        /// <summary>引擎切换确认弹窗；返回是否确认切换。</summary>
        bool ShowEngineSwitch(EngineCardDefinition card);

        /// <summary>添加检测引擎弹窗（自定义模型接入）；返回是否创建成功。</summary>
        bool ShowAddEngine();

        /// <summary>存档详情弹窗（审计页"⋯"）。</summary>
        void ShowShot(AIris.Core.Model.AuditRecord record);

        /// <summary>ROI 标画全屏弹窗（配方编辑器画布"全屏标画"图标钮）。</summary>
        void ShowRoiFullscreen(RecipeEditorViewModel vm);

        /// <summary>信息提示（替代 MessageBox 直调，统一弹窗壳）。</summary>
        void Info(string text, string caption = "AI 智瞳");
    }

    /// <summary>
    /// 服务装配中枢（静态门面，AIris 规模小阶段的最简装配点；规模大了再实例化成 AppHost）：
    /// 持有存储/设置/运行编排/主窗 VM/宿主门面。App.OnStartup 装配一次，全局只读使用。
    /// </summary>
    public static class ServiceHub
    {
        /// <summary>设置入口（MainSetting.ini 强类型读写）。</summary>
        public static AppSettings Settings { get; private set; }

        /// <summary>配方存储。</summary>
        public static RecipeStore Recipes { get; private set; }

        /// <summary>引擎卡存储（自定义引擎卡 + 内置卡参数覆盖，Data\EngineCards.json）。</summary>
        public static EngineCardStore EngineCards { get; private set; }

        /// <summary>运行编排（采集→检测→判定→工步→审计）。</summary>
        public static RunMonitor Monitor { get; private set; }

        /// <summary>主窗 VM（页面导航/回跳）。</summary>
        public static MainViewModel Main { get; private set; }

        /// <summary>宿主开窗门面。</summary>
        public static IHost Host { get; private set; }

        /// <summary>
        /// 全局 Mock 数据模式（[App] MockMode）：true = 相机与检测引擎都使用内置演示数据。
        /// 唯一切换入口 <see cref="SetMockMode"/>；各页只读本属性决定提示/演示控制条显隐。
        /// </summary>
        public static bool MockMode { get; private set; }

        /// <summary>Mock 数据模式切换后触发（UI 线程调用；订阅方自行 Dispatcher 调度）。</summary>
        public static event Action<bool> MockModeChanged;

        /// <summary>
        /// App.OnStartup 装配（唯一调用点）。
        /// 相机/引擎按全局 Mock 数据模式装配：ON = Mock 相机 + Mock 引擎（演示）；
        /// OFF = 设备页配置的真实相机 + 引擎页选中的真实引擎卡。
        /// </summary>
        public static void Install(MainViewModel mainVm)
        {
            AppPaths.EnsureDirs();
            Settings = new AppSettings();
            Recipes = new RecipeStore();
            EngineCards = new EngineCardStore();

            // 全局 Mock 开关先读（内部完成旧版「相机/引擎卡 = mock」→ MockMode=ON 的首次迁移）
            MockMode = Settings.ReadMockMode();

            var cfg0 = Settings.ReadEngine();   // 协议初值注入（cfg.Protocol 空时按引擎卡默认）
            // 旧版 ActiveEngineId=mock 且 Mock 卡已删除：迁移到当前商用主力卡，
            // 这样关闭 Mock 数据模式后有明确的真实引擎目标（不可达时按既有降级语义回落）。
            if (MigrateLegacyMockEngineSelection(cfg0)) cfg0 = Settings.ReadEngine();

            // 首次升级迁移（2026-11-23 引擎卡独立参数）：旧版 INI 只有一套全局 HTTP 参数，
            // 本卡还没有参数记录时把它落到当前引擎卡上（只发生一次；之后以卡参数为权威）。
            MigrateEngineCardParams(cfg0);

            var cam = CreateCamera(MockMode);
            var engine = MockMode
                ? (IDetectionEngine)new MockDetectionEngine()
                : EngineRegistry.Create(cfg0.ActiveEngineId, cfg0, EngineCards);
            var audit = new JsonAuditStore();
            Monitor = new RunMonitor(cam, engine, audit);
            Monitor.Log += m => AppLog.Info(m);
            // 按设置装载引擎参数（Mock 引擎 Load 只清场景；HTTP 引擎装载后走降级回落）
            Monitor.SwapEngine(engine, cfg0);

            Main = mainVm;
            Host = new AppHost(mainVm);
            mainVm.AttachRuntime();   // 顶栏状态胶囊订阅（此时 Monitor 已装配；构造器里还拿不到）
        }

        /// <summary>
        /// 切换全局 Mock 数据模式（顶栏「Mock 数据」按钮唯一入口）：
        /// 持久化 → 相机/引擎热切换（运行中不中断检测线程）→ 通知各页刷新。
        ///
        /// 关闭时按 INI 里保存的真实相机与真实引擎卡恢复；真实设备不可达时由运行层按既有
        /// 语义如实呈现（相机离线红点 / 引擎探活失败回落 Mock 橙点），这里不伪造在线。
        /// </summary>
        public static void SetMockMode(bool on)
        {
            if (MockMode == on) return;
            MockMode = on;
            Settings.WriteMockMode(on);

            if (Monitor != null)
            {
                // 相机：ON → 合成画面；OFF → 设备页配置的真机（RTSP/GigE）
                Monitor.SwapCamera(CreateCamera(on));

                // 引擎：ON → 演示引擎；OFF → 当前引擎卡（探活失败由 RunMonitor 回落并明示）
                var cfg = Settings.ReadEngine();
                var engine = on
                    ? (IDetectionEngine)new MockDetectionEngine()
                    : EngineRegistry.Create(cfg.ActiveEngineId, cfg, EngineCards);
                Monitor.SwapEngine(engine, cfg);
            }

            var h = MockModeChanged;
            if (h != null) h(on);
        }

        /// <summary>
        /// 按设备配置装配相机源（启动时一次；设备页保存后热切换）：
        ///   mockMode = true → Mock 合成画面（全局 Mock 数据模式）
        ///   rtsp = RTSP 网络相机（安格华等，复用 Kaleidoscope RtspFrameCapturer 拉流）
        ///   hik  = 海康 GigE 工业相机（SDK 桩，真机接入时填实）
        /// 读取配置失败（INI 损坏等）回落 Mock，保证程序能起来（兜底，不是 Mock 选项）。
        /// </summary>
        private static ICameraSource CreateCamera(bool mockMode)
        {
            if (mockMode) return new MockCameraSource();

            DeviceConfig dev;
            try
            {
                dev = Settings.ReadDevices();
            }
            catch
            {
                return new MockCameraSource();
            }

            string kind = (dev.CameraKind ?? "rtsp").Trim().ToLowerInvariant();
            switch (kind)
            {
                case "hik":
                    return new HikCameraSourceStub();
                default:
                    return new RtspCameraSource(dev);
            }
        }

        /// <summary>
        /// 设备页保存后应用相机配置（热切换，不用重启程序）：
        /// 按最新 INI 重建相机源，替换运行中的监控器相机（RunMonitor.SwapCamera 内部处理启停）。
        /// Mock 数据模式期间保持合成相机不动——真实参数照常落盘，关闭 Mock 后自动生效。
        /// </summary>
        public static ICameraSource ApplyCamera()
        {
            if (Monitor == null) return null;
            if (MockMode) return Monitor.Camera;

            var next = CreateCamera(false);
            Monitor.SwapCamera(next);
            return next;
        }

        /// <summary>
        /// 旧版 ActiveEngineId=mock 迁移：Mock 卡已从引擎卡列删除，把选择落到当前商用主力卡
        /// （Qwen3-VL-8B），并清空协议让 EngineRegistry 按卡默认注入。
        /// 只在 Mock 模式关闭后才有实际连接行为；Mock 模式期间不探活、不打扰。
        /// 返回是否发生了迁移（调用方据此重读 INI）。
        /// </summary>
        private static bool MigrateLegacyMockEngineSelection(EngineConfig cfg)
        {
            if (cfg == null || !string.Equals(cfg.ActiveEngineId, "mock", StringComparison.OrdinalIgnoreCase))
                return false;

            var def = EngineRegistry.Find("qwen3-vl-http", EngineCards);
            if (def == null) return false;

            cfg.ActiveEngineId = def.Id;
            cfg.Protocol = def.Protocol;
            cfg.HttpBaseUrl = def.HttpBaseUrl;
            cfg.HttpModel = def.HttpModel;
            cfg.ApiKey = def.ApiKey;
            cfg.HttpTimeoutMs = def.HttpTimeoutMs;
            Settings.WriteEngine(cfg);
            return true;
        }

        /// <summary>
        /// 首次升级迁移：旧版 INI 的全局 HTTP 参数 → 当前引擎卡（按 id 存 Data\EngineCards.json）。
        /// 已有参数记录则跳过（之后"应用配置"/切换都会维护卡参数，卡是权威）。
        /// </summary>
        private static void MigrateEngineCardParams(EngineConfig cfg)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.ActiveEngineId)) return;
            if (string.Equals(cfg.ActiveEngineId, "mock", StringComparison.OrdinalIgnoreCase)) return;
            if (EngineCards.HasParams(cfg.ActiveEngineId)) return;

            var def = EngineRegistry.Find(cfg.ActiveEngineId, EngineCards);
            if (def == null || def.IsMock) return;
            if (!string.IsNullOrEmpty(cfg.Protocol)) def.Protocol = cfg.Protocol;
            if (!string.IsNullOrEmpty(cfg.HttpBaseUrl)) def.HttpBaseUrl = cfg.HttpBaseUrl;
            if (!string.IsNullOrEmpty(cfg.HttpModel)) def.HttpModel = cfg.HttpModel;
            if (!string.IsNullOrEmpty(cfg.ApiKey)) def.ApiKey = cfg.ApiKey;
            if (cfg.HttpTimeoutMs > 0) def.HttpTimeoutMs = cfg.HttpTimeoutMs;
            EngineCards.SaveParams(def);
        }

        /// <summary>最近一条 NG 审计记录（NG 详情弹窗回显截图用；无则 null）。</summary>
        public static AIris.Core.Model.AuditRecord AuditLatestNgShot()
        {
            var list = new JsonAuditStore().Query(null, "NG", null, 1);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>AppHost 实现（开窗/回跳的唯一 new Window 点）。</summary>
        private sealed class AppHost : IHost
        {
            private readonly MainViewModel _main;
            public AppHost(MainViewModel main) { _main = main; }

            public bool ShowNewRecipe()
            {
                var dlg = new Views.NewRecipeDialog();
                dlg.Owner = Owner();
                return dlg.ShowDialog() == true;
            }

            public void GoEditor(AIris.Core.Model.Recipe recipe)
            {
                // 编辑器是单实例页 VM：静态注入待编辑配方 → 切页（OnEnter 读取）
                RecipeEditorViewModel.CurrentEditing = recipe;
                var page = Main.FindPage("editor");
                if (page != null) _main.Go(page);
            }

            public bool Confirm(string title, string text, string confirmText, bool danger)
            {
                var dlg = new Views.ConfirmDialog(title, text, confirmText, danger);
                dlg.Owner = Owner();
                return dlg.ShowDialog() == true;
            }

            public void ShowNgDetail(VerdictEventArgs verdict, AIris.Core.Model.Recipe recipe)
            {
                var dlg = new Views.NgDetailDialog(verdict, recipe);
                dlg.Owner = Owner();
                dlg.ShowDialog();
            }

            public bool ShowEngineSwitch(EngineCardDefinition card)
            {
                string mockNote = MockMode
                    ? "\r\n\r\n当前为 Mock 数据模式：确认切换将同时关闭 Mock（相机切回设备页配置的真实相机）。"
                    : "";
                return Confirm("切换检测引擎",
                    "切换到 " + card.Name + " 将卸载当前引擎并重新加载。\r\n许可证：" + card.LicenseNote
                    + "\r\n服务参数：" + card.Protocol + " · " + card.HttpBaseUrl + " · " + card.HttpModel
                    + "\r\n未接入时检测自动回落 Mock 演示（流程不中断，画面信息行会标注 →Mock）。"
                    + mockNote,
                    "确认切换", false);
            }

            public bool ShowAddEngine()
            {
                var dlg = new Views.AddEngineDialog();
                dlg.Owner = Owner();
                return dlg.ShowDialog() == true;
            }

            public void ShowShot(AIris.Core.Model.AuditRecord record)
            {
                var dlg = new Views.ShotDialog(record);
                dlg.Owner = Owner();
                dlg.ShowDialog();
            }

            /// <summary>ROI 全屏标画窗（模态：编辑完关窗回编辑器；同一 VM 保证双向同步）。</summary>
            public void ShowRoiFullscreen(RecipeEditorViewModel vm)
            {
                if (vm == null) return;
                var dlg = new Views.RoiFullscreenDialog(vm);
                dlg.Owner = Owner();
                dlg.ShowDialog();
            }

            public void Info(string text, string caption = "AI 智瞳")
            {
                var dlg = new Views.ConfirmDialog(caption, text, "知道了", false, hideCancel: true);
                dlg.Owner = Owner();
                dlg.ShowDialog();
            }

            private System.Windows.Window Owner()
            {
                return _main != null && System.Windows.Application.Current != null
                    ? System.Windows.Application.Current.MainWindow : null;
            }
        }
    }
}
