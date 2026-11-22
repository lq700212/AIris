namespace AIris.Core.Model
{
    /// <summary>
    /// 检测引擎配置（MainSetting.ini [Engine] 节；检测引擎页读写）。
    /// 大模型未接入时 ActiveEngineId = mock；接入真实服务后填 HttpBaseUrl/HttpModel。
    /// </summary>
    public class EngineConfig
    {
        /// <summary>当前引擎 id（mock / locateanything-http / qwen-vl-http，见 EngineRegistry）。</summary>
        public string ActiveEngineId { get; set; } = "mock";

        /// <summary>HTTP 推理服务地址（未来 FastAPI 常驻服务，如 http://127.0.0.1:8100）。</summary>
        public string HttpBaseUrl { get; set; } = "";

        /// <summary>HTTP 服务模型名（未来服务端路由用，如 locateanything-3b / qwen2.5-vl-7b）。</summary>
        public string HttpModel { get; set; } = "";

        /// <summary>推理超时 ms（默认 5000：单帧 430ms 量级，留足余量）。</summary>
        public int HttpTimeoutMs { get; set; } = 5000;

        /// <summary>解码模式（对应 locateanything 三档：fast/slow/hybrid，设计稿 engine 页下拉）。</summary>
        public string DecodeMode { get; set; } = "hybrid";

        /// <summary>批处理大小（对应 locateanything-batch，默认 8）。</summary>
        public int BatchSize { get; set; } = 8;
    }

    /// <summary>
    /// 设备参数（MainSetting.ini [Camera]/[Light]/[Hmi]/[Mes] 节；设备与集成页读写）。
    /// 全部为"参数落盘 + Mock 生效"形态，真机时替换实现不改字段。
    /// </summary>
    public class DeviceConfig
    {
        // —— 相机（对位设计稿 settings 页相机参数） ——
        public string CameraIp { get; set; } = "192.168.1.64";
        public string CameraPort { get; set; } = "8080";
        public string CameraPixelFormat { get; set; } = "Mono8（黑白）";
        public int CameraExposureUs { get; set; } = 8000;
        public string CameraTrigger { get; set; } = "软触发 · 12 fps";

        // —— 光源 ——
        public int LightBrightness { get; set; } = 72;
        public bool LightDiffuserOn { get; set; } = true;   // 漫射罩常开（调研报告 §5.2：高角度环形光+漫射罩防金属反光）

        // —— 引导屏 / 声光 ——
        public bool GuideScreenOn { get; set; } = true;     // 引导屏同步工步文案
        public bool BeaconOn { get; set; } = true;          // 三色声光报警灯

        // —— MES（HTTP 对接，参数先落盘） ——
        public string MesUrl { get; set; } = "";
        public bool MesEnabled { get; set; }
    }
}
