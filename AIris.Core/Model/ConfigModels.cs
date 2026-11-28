namespace AIris.Core.Model
{
    /// <summary>
    /// 检测引擎配置（MainSetting.ini [Engine] 节；检测引擎页读写）。
    /// 大模型未接入时 ActiveEngineId = mock；接入真实服务后填 HttpBaseUrl/HttpModel。
    /// </summary>
    public class EngineConfig
    {
        /// <summary>当前引擎 id（mock / locateanything-http / qwen-vl-http / qwen3-vl-http / custom-xxxxxxxx，见 EngineRegistry）。</summary>
        public string ActiveEngineId { get; set; } = "mock";

        /// <summary>推理服务地址（FastAPI 常驻服务 / vLLM / llama-server / Ollama，如 http://127.0.0.1:8000）。</summary>
        public string HttpBaseUrl { get; set; } = "";

        /// <summary>HTTP 服务模型名（服务端路由用，如 Qwen/Qwen3-VL-8B-Instruct / qwen2.5-vl-7b / locateanything-3b）。</summary>
        public string HttpModel { get; set; } = "";

        /// <summary>
        /// 推理协议（V0.1.7 沉库 Kaleidoscope.Infer 后可切）：custom=自研 /detect 契约（默认）；
        /// openai=OpenAI 兼容（vLLM/llama.cpp/LMDeploy）；anthropic=Anthropic /v1/messages；
        /// ollama=Ollama 原生 /api/chat。默认按引擎卡（EngineRegistry 建引擎时注入初值；用户可改下拉覆盖）。
        /// </summary>
        public string Protocol { get; set; } = "custom";

        /// <summary>API Key（云服务/网关鉴权；本地服务留空）。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>推理超时 ms（默认 5000：单帧 430ms 量级，留足余量）。</summary>
        public int HttpTimeoutMs { get; set; } = 5000;

        /// <summary>解码模式（对应 locateanything 三档：fast/slow/hybrid，设计稿 engine 页下拉）。</summary>
        public string DecodeMode { get; set; } = "hybrid";

        /// <summary>批处理大小（对应 locateanything-batch，默认 8）。</summary>
        public int BatchSize { get; set; } = 8;

        /// <summary>语义决策通道开关（V0.1.7 JEV 留口；默认关——云端决策不进离线主链路）。</summary>
        public bool SemanticDecisionEnabled { get; set; } = false;

        /// <summary>语义决策服务端点（Decisions 协议；空=未配置）。</summary>
        public string SemanticBaseUrl { get; set; } = "";
    }

    /// <summary>
    /// 设备参数（MainSetting.ini [Camera]/[Light]/[Hmi]/[Mes] 节；设备与集成页读写）。
    /// 全部为"参数落盘 + Mock 生效"形态，真机时替换实现不改字段。
    /// </summary>
    public class DeviceConfig
    {
        // —— 相机（对位设计稿 settings 页相机参数） ——

        /// <summary>
        /// 相机类型（决定 ServiceHub 装配哪个 ICameraSource）：
        ///   mock = Mock 合成画面（默认，演示/无相机时用）
        ///   rtsp = RTSP 网络相机（安格华等 IP 相机，走 OpenCV VideoCapture 拉流）
        ///   hik  = 海康 GigE 工业相机（SDK 桩，待真机接入）
        /// </summary>
        public string CameraKind { get; set; } = "mock";

        public string CameraIp { get; set; } = "192.168.1.64";

        /// <summary>端口：RTSP 标准 554（网络相机），海康 GigE 用 SDK 不用端口。</summary>
        public string CameraPort { get; set; } = "554";

        public string CameraPixelFormat { get; set; } = "Mono8（黑白）";
        public int CameraExposureUs { get; set; } = 8000;
        public string CameraTrigger { get; set; } = "软触发 · 12 fps";

        /// <summary>RTSP 通道号（安格华相机 stream{channel}，0 起）。</summary>
        public int RtspChannel { get; set; }

        /// <summary>
        /// RTSP 地址模板：占位符 {ip}/{port}/{channel}。
        /// 安格华默认 rtsp://{ip}:{port}/stream{channel}；其他品牌按实际地址改模板即可。
        /// </summary>
        public string RtspUrlFormat { get; set; } = "rtsp://{ip}:{port}/stream{channel}";

        // —— 光源 ——
        public int LightBrightness { get; set; } = 72;
        public bool LightDiffuserOn { get; set; } = true;   // 漫射罩常开（落地指南 §2.7：高角度环形光+漫射罩防金属反光）

        // —— 引导屏 / 声光 ——
        public bool GuideScreenOn { get; set; } = true;     // 引导屏同步工步文案
        public bool BeaconOn { get; set; } = true;          // 三色声光报警灯

        // —— MES（HTTP 对接，参数先落盘） ——
        public string MesUrl { get; set; } = "";
        public bool MesEnabled { get; set; }
    }
}
