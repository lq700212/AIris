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

        // —— 引擎配置（[Engine]） ——
        public EngineConfig ReadEngine()
        {
            return new EngineConfig
            {
                ActiveEngineId = _ini.Read("Engine", "ActiveEngineId", "mock"),
                HttpBaseUrl = _ini.Read("Engine", "HttpBaseUrl", ""),
                HttpModel = _ini.Read("Engine", "HttpModel", ""),
                HttpTimeoutMs = ParseInt(_ini.Read("Engine", "HttpTimeoutMs", "5000"), 5000),
                DecodeMode = _ini.Read("Engine", "DecodeMode", "hybrid"),
                BatchSize = ParseInt(_ini.Read("Engine", "BatchSize", "8"), 8)
            };
        }

        public void WriteEngine(EngineConfig cfg)
        {
            _ini.Write("Engine", "ActiveEngineId", cfg.ActiveEngineId);
            _ini.Write("Engine", "HttpBaseUrl", cfg.HttpBaseUrl);
            _ini.Write("Engine", "HttpModel", cfg.HttpModel);
            _ini.Write("Engine", "HttpTimeoutMs", cfg.HttpTimeoutMs.ToString());
            _ini.Write("Engine", "DecodeMode", cfg.DecodeMode);
            _ini.Write("Engine", "BatchSize", cfg.BatchSize.ToString());
        }

        // —— 设备参数（[Camera]/[Light]/[Hmi]/[Mes]） ——
        public DeviceConfig ReadDevices()
        {
            return new DeviceConfig
            {
                CameraIp = _ini.Read("Camera", "Ip", "192.168.1.64"),
                CameraPort = _ini.Read("Camera", "Port", "8080"),
                CameraPixelFormat = _ini.Read("Camera", "PixelFormat", "Mono8（黑白）"),
                CameraExposureUs = ParseInt(_ini.Read("Camera", "ExposureUs", "8000"), 8000),
                CameraTrigger = _ini.Read("Camera", "Trigger", "软触发 · 12 fps"),
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
            _ini.Write("Camera", "Ip", d.CameraIp);
            _ini.Write("Camera", "Port", d.CameraPort);
            _ini.Write("Camera", "PixelFormat", d.CameraPixelFormat);
            _ini.Write("Camera", "ExposureUs", d.CameraExposureUs.ToString());
            _ini.Write("Camera", "Trigger", d.CameraTrigger);
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
    }
}
