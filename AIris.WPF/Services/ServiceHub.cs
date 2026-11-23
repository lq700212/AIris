using System;
using AIris.Core.Camera;
using AIris.Core.Config;
using AIris.Core.Detect;
using AIris.Core.Engine;
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
        bool ShowEngineSwitch(EngineRegistry.EngineCard card);

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

        /// <summary>运行编排（采集→检测→判定→工步→审计）。</summary>
        public static RunMonitor Monitor { get; private set; }

        /// <summary>主窗 VM（页面导航/回跳）。</summary>
        public static MainViewModel Main { get; private set; }

        /// <summary>宿主开窗门面。</summary>
        public static IHost Host { get; private set; }

        /// <summary>
        /// App.OnStartup 装配（唯一调用点）。默认装配：Mock 相机 + Mock 检测引擎 + JSONL 审计。
        /// </summary>
        public static void Install(MainViewModel mainVm)
        {
            AppPaths.EnsureDirs();
            Settings = new AppSettings();
            Recipes = new RecipeStore();

            var cam = new MockCameraSource();
            var cfg0 = Settings.ReadEngine();   // 协议初值注入（cfg.Protocol 空时按引擎卡默认）
            var engine = EngineRegistry.Create(cfg0.ActiveEngineId, cfg0);
            var audit = new JsonAuditStore();
            Monitor = new RunMonitor(cam, engine, audit);
            Monitor.Log += m => AppLog.Info(m);
            // 按设置装载引擎参数（Mock 引擎 Load 只清场景；HTTP 引擎装载后走降级回落）
            Monitor.SwapEngine(engine, cfg0);

            Main = mainVm;
            Host = new AppHost(mainVm);
            mainVm.AttachRuntime();   // 顶栏引擎态胶囊订阅（此时 Monitor 已装配；构造器里还拿不到）
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

            public bool ShowEngineSwitch(EngineRegistry.EngineCard card)
            {
                return Confirm("切换检测引擎",
                    "切换到 " + card.Name + " 将卸载当前引擎并重新加载。\r\n许可证：" + card.LicenseNote
                    + "\r\n未接入时检测自动回落 Mock 演示（流程不中断，画面信息行会标注 →Mock）。",
                    "确认切换", false);
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
