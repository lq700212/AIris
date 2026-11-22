using System;
using System.Collections.Generic;
using System.IO;
using AIris.Core.Config;

namespace AIris.WPF.Services
{
    /// <summary>UI 日志行（纯数据，无 UI 依赖；颜色由 XAML 按 Level DataTrigger 着色）。</summary>
    public class LogLine
    {
        public string Time { get; set; } = "";
        public string Level { get; set; } = "";
        public string Msg { get; set; } = "";
    }

    /// <summary>
    /// AIris 运行日志（双出口）：①文本行落盘（Logs\运行日志_yyyyMMdd.log，按天滚动）；
    /// ②UI 实时流——环形缓冲（最近 300 条）+ LineAdded 事件，监控台"运行日志"面板
    /// 订阅显示（订阅方自行 Dispatcher 切线程，事件可能在检测线程触发）。
    /// Kaleidoscope 的 LogHelper.LogAction / DeviceServiceBase.LogSink 在
    /// <see cref="LogSinkInjector"/> 里统一接到本类，库内日志与宿主日志一个出口。
    /// </summary>
    public static class AppLog
    {
        private static readonly object _sync = new object();
        private static readonly Queue<LogLine> _ring = new Queue<LogLine>();
        /// <summary>UI 流容量（环形缓冲与 VM 集合同限，防长跑内存膨胀）。</summary>
        private const int RingCapacity = 300;

        /// <summary>新日志行事件（任意线程触发；UI 订阅方自行 Dispatcher 收口）。</summary>
        public static event Action<LogLine> LineAdded;

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }
        public static void Error(string msg, Exception ex) { Write("ERROR", msg + " :: " + ex); }

        /// <summary>首屏回放：环形缓冲快照（VM 初始化时回填，之后的行走 LineAdded）。</summary>
        public static LogLine[] Snapshot()
        {
            lock (_sync) { return _ring.ToArray(); }
        }

        private static void Write(string level, string msg)
        {
            var line = new LogLine { Time = DateTime.Now.ToString("HH:mm:ss"), Level = level, Msg = msg };
            try
            {
                lock (_sync)
                {
                    try
                    {
                        var path = Path.Combine(AppPaths.LogDir, "运行日志_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                        File.AppendAllText(path,
                            DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine,
                            System.Text.Encoding.UTF8);
                    }
                    catch { /* 落盘失败静默：日志永远不能反过来弄崩业务 */ }
                    _ring.Enqueue(line);
                    while (_ring.Count > RingCapacity) _ring.Dequeue();
                }
                var h = LineAdded;
                if (h != null) h(line);
            }
            catch { /* UI 流失败静默，不影响落盘链路 */ }
        }
    }

    /// <summary>Kaleidoscope 库日志出口注入（App 启动链调用一次）。</summary>
    public static class LogSinkInjector
    {
        public static void Install()
        {
            // 库日志 → AIris 统一日志；设置 LogAction 后库自带的文件落盘自动跳过
            Kaleidoscope.Utils.LogHelper.LogAction = (level, message) =>
            {
                if (level == "E") AppLog.Error(message);
                else if (level == "W") AppLog.Warn(message);
                else AppLog.Info(message);
            };
            // 全设备共用日志注入点（库 DeviceServiceBase 静态 LogSink，null=静默）
            Kaleidoscope.Services.DeviceServiceBase.LogSink = msg => AppLog.Info(msg);
            AppLog.Info("Kaleidoscope 日志出口已接入 AIris 统一日志");
        }
    }

    /// <summary>Kaleidoscope Mock 桥注入（App 启动链调用一次；AIris 暂无 PLC 业务，按库契约接好口子）。</summary>
    public static class MockBridgeInjector
    {
        public static void Install()
        {
            // Mock 总开关：MainSetting.ini [Mock] Enabled，默认 ON（新装环境无真设备，先全 Mock 演示）
            var ini = new Core.Config.IniFile(AppPaths.MainIni);
            bool enabled = ini.ReadFlag("Mock", "Enabled", true);
            Kaleidoscope.Utils.MockBridge.IsEnabledProvider = () => enabled;
            // 假读契约：永不返回 null；AIris 无 PLC 点表，统一回 "0"
            Kaleidoscope.Utils.MockBridge.FakePlcReadProvider = (dev, addr, kind) => "0";
            // 假写：演示态静默丢弃
            Kaleidoscope.Utils.MockBridge.FakePlcWriteAction = (dev, addr, text) => { };
            AppLog.Info("MockBridge 已注入，Mock 总开关=" + enabled);
        }
    }
}
