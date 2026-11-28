using System;
using AIris.Core.Config;
using AIris.Core.Model;

namespace AIris.Core.Store
{
    /// <summary>
    /// 主设置入口：MainSetting.ini 各节的强类型读写（引擎/设备参数，各页与 Services 共享）。
    /// 开关统一 ON/OFF 口径（对位 HuaJiVision ISettingsStore 语义）。
    /// </summary>
    public class AppSettings
    {
        private readonly IniFile _ini;

        public AppSettings() { _ini = new IniFile(AppPaths.MainIni); }

        /// <summary>底层 INI（主题开关/其它零散键直接走这里）。</summary>
        public IniFile Ini { get { return _ini; } }

        // —— 全局 Mock 数据模式（[App]） ——

        /// <summary>
        /// 全局 Mock 数据模式（[App] MockMode）：ON = 相机与检测引擎都使用内置演示数据。
        /// 这是「没有相机/没有模型时看效果」的唯一开关（顶栏「Mock 数据」按钮驱动）。
        ///
        /// 【首次升级迁移】旧版把 Mock 分散在「相机类型 = mock」「引擎卡 = mock」两处；
        /// 任一命中即视为原本就在演示态 → 自动写 MockMode=ON（只发生一次）。
        /// 之后相机类型下拉只保留 RTSP/GigE，引擎卡只保留真实模型，Mock 语义完全收口到本键。
        /// </summary>
        public bool ReadMockMode()
        {
            string raw = _ini.Read("App", "MockMode", "");
            if (!string.IsNullOrEmpty(raw)) return _ini.ReadFlag("App", "MockMode", false);

            bool legacy = string.Equals(_ini.Read("Camera", "Kind", ""), "mock", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(_ini.Read("Engine", "ActiveEngineId", ""), "mock", StringComparison.OrdinalIgnoreCase);
            _ini.Write("App", "MockMode", legacy ? "ON" : "OFF");
            return legacy;
        }

        /// <summary>写全局 Mock 数据模式（顶栏切换时调用；运行期由 ServiceHub.SetMockMode 统一收口）。</summary>
        public void WriteMockMode(bool on)
        {
            _ini.Write("App", "MockMode", on ? "ON" : "OFF");
        }

        // —— 引擎配置（[Engine]） ——
        public EngineConfig ReadEngine()
        {
            return new EngineConfig
            {
                ActiveEngineId = _ini.Read("Engine", "ActiveEngineId", "qwen3-vl-http"),
                HttpBaseUrl = _ini.Read("Engine", "HttpBaseUrl", ""),
                HttpModel = _ini.Read("Engine", "HttpModel", ""),
                Protocol = _ini.Read("Engine", "Protocol", ""),         // 空 = 按引擎卡默认注入（EngineRegistry）
                ApiKey = _ini.Read("Engine", "ApiKey", ""),
                HttpTimeoutMs = ParseInt(_ini.Read("Engine", "HttpTimeoutMs", "5000"), 5000),
                DecodeMode = _ini.Read("Engine", "DecodeMode", "hybrid"),
                BatchSize = ParseInt(_ini.Read("Engine", "BatchSize", "8"), 8),
                SemanticDecisionEnabled = _ini.ReadFlag("Engine", "SemanticDecisionEnabled", false),
                SemanticBaseUrl = _ini.Read("Engine", "SemanticBaseUrl", "")
            };
        }

        public void WriteEngine(EngineConfig cfg)
        {
            _ini.Write("Engine", "ActiveEngineId", cfg.ActiveEngineId);
            _ini.Write("Engine", "HttpBaseUrl", cfg.HttpBaseUrl);
            _ini.Write("Engine", "HttpModel", cfg.HttpModel);
            _ini.Write("Engine", "Protocol", cfg.Protocol ?? "");
            _ini.Write("Engine", "ApiKey", cfg.ApiKey ?? "");
            _ini.Write("Engine", "HttpTimeoutMs", cfg.HttpTimeoutMs.ToString());
            _ini.Write("Engine", "DecodeMode", cfg.DecodeMode);
            _ini.Write("Engine", "BatchSize", cfg.BatchSize.ToString());
            _ini.Write("Engine", "SemanticDecisionEnabled", cfg.SemanticDecisionEnabled ? "ON" : "OFF");
            _ini.Write("Engine", "SemanticBaseUrl", cfg.SemanticBaseUrl ?? "");
        }

        // —— 设备参数（[Camera]/[Light]/[Hmi]/[Mes]） ——
        public DeviceConfig ReadDevices()
        {
            return new DeviceConfig
            {
                // 相机类型只剩真实设备两档（rtsp / hik）；旧版 "mock" 归一到 rtsp，
                // Mock 演示由 [App] MockMode 独立承载（见 ReadMockMode 的首次迁移）。
                CameraKind = NormalizeCameraKind(_ini.Read("Camera", "Kind", "rtsp")),
                CameraIp = _ini.Read("Camera", "Ip", "192.168.1.64"),
                CameraPort = _ini.Read("Camera", "Port", "554"),
                CameraPixelFormat = _ini.Read("Camera", "PixelFormat", "Mono8（黑白）"),
                CameraExposureUs = ParseInt(_ini.Read("Camera", "ExposureUs", "8000"), 8000),
                CameraTrigger = _ini.Read("Camera", "Trigger", "软触发 · 12 fps"),
                RtspChannel = ParseInt(_ini.Read("Camera", "RtspChannel", "0"), 0),
                RtspUrlFormat = _ini.Read("Camera", "RtspUrlFormat", "rtsp://{ip}:{port}/stream{channel}"),
                LightBrightness = ParseInt(_ini.Read("Light", "Brightness", "72"), 72),
                LightDiffuserOn = _ini.ReadFlag("Light", "DiffuserOn", true),
                GuideScreenOn = _ini.ReadFlag("Hmi", "GuideScreenOn", true),
                BeaconOn = _ini.ReadFlag("Hmi", "BeaconOn", true),
                MesUrl = _ini.Read("Mes", "Url", ""),
                MesEnabled = _ini.ReadFlag("Mes", "Enabled", false)
            };
        }

        public void WriteDevices(DeviceConfig d)
        {
            _ini.Write("Camera", "Kind", NormalizeCameraKind(d.CameraKind));
            _ini.Write("Camera", "Ip", d.CameraIp);
            _ini.Write("Camera", "Port", d.CameraPort);
            _ini.Write("Camera", "PixelFormat", d.CameraPixelFormat);
            _ini.Write("Camera", "ExposureUs", d.CameraExposureUs.ToString());
            _ini.Write("Camera", "Trigger", d.CameraTrigger);
            _ini.Write("Camera", "RtspChannel", d.RtspChannel.ToString());
            _ini.Write("Camera", "RtspUrlFormat", d.RtspUrlFormat ?? "rtsp://{ip}:{port}/stream{channel}");
            _ini.Write("Light", "Brightness", d.LightBrightness.ToString());
            _ini.WriteFlag("Light", "DiffuserOn", d.LightDiffuserOn);
            _ini.WriteFlag("Hmi", "GuideScreenOn", d.GuideScreenOn);
            _ini.WriteFlag("Hmi", "BeaconOn", d.BeaconOn);
            _ini.Write("Mes", "Url", d.MesUrl);
            _ini.WriteFlag("Mes", "Enabled", d.MesEnabled);
        }

        private static int ParseInt(string s, int def)
        {
            int v; return int.TryParse(s, out v) ? v : def;
        }

        /// <summary>
        /// 相机类型归一：只认 rtsp / hik 两档真实设备。
        /// 旧版 "mock"（以及任何未知值）一律落到 rtsp——Mock 演示不再由相机类型表达，
        /// 而是全局 [App] MockMode；这样设备页下拉不会出现"没有选中项"的空框。
        /// </summary>
        private static string NormalizeCameraKind(string kind)
        {
            return string.Equals((kind ?? "").Trim(), "hik", StringComparison.OrdinalIgnoreCase)
                ? "hik" : "rtsp";
        }
    }
}
