using System;
using System.IO;

namespace AIris.Core.Config
{
    /// <summary>
    /// 运行目录约定（AGENTS.md 铁律 4：00_ExeBuild = 编译输出兼运行目录）。
    /// 所有运行数据都在 exe 目录树下，路径统一从这里取，禁止散落各处拼字符串。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>exe 所在运行目录（00_ExeBuild）。</summary>
        public static string BaseDir { get; } = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>主配置目录：MainSetting\</summary>
        public static string SettingDir => Path.Combine(BaseDir, "MainSetting");

        /// <summary>主配置文件：MainSetting\MainSetting.ini（相机/引擎/设备/主题开关）。</summary>
        public static string MainIni => Path.Combine(SettingDir, "MainSetting.ini");

        /// <summary>配方根目录：Data\Recipes\，每产品一个 &lt;名称&gt;.json。</summary>
        public static string RecipeDir => Path.Combine(BaseDir, "Data", "Recipes");

        /// <summary>引擎卡文件：Data\EngineCards.json（内置卡参数覆盖 + 自定义引擎卡）。</summary>
        public static string EngineCardsFile => Path.Combine(BaseDir, "Data", "EngineCards.json");

        /// <summary>审计存根目录：Data\Audit\，JSONL 按天 + 截图 PNG。</summary>
        public static string AuditDir => Path.Combine(BaseDir, "Data", "Audit");

        /// <summary>审计截图子目录：Data\Audit\Shots\。</summary>
        public static string AuditShotDir => Path.Combine(AuditDir, "Shots");

        /// <summary>运行日志目录：Logs\。</summary>
        public static string LogDir => Path.Combine(BaseDir, "Logs");

        /// <summary>启动时确保全部运行目录存在（App.OnStartup 早期调用一次）。</summary>
        public static void EnsureDirs()
        {
            foreach (var d in new[] { SettingDir, RecipeDir, AuditDir, AuditShotDir, LogDir })
            {
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            }
        }
    }
}
