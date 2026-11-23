using System;

namespace AIris.Core.Model
{
    /// <summary>
    /// 引擎卡定义（设计稿 data-page="engine" 的卡片模型；2026-11-23 vNext「引擎卡扩展」）。
    ///
    /// 【两种来源】
    ///  - 内置卡：EngineRegistry.Builtin 代码内预设（Mock / LocateAnything / Qwen2.5 / Qwen3），不可删除；
    ///  - 自定义卡：引擎页「＋ 添加引擎」创建，落 Data\EngineCards.json，可删除。
    ///
    /// 【每卡独立连接参数】卡上直接携带 协议/地址/模型名/Key/超时——切换引擎时回填推理参数表单并落 INI，
    /// 现场换模型不用重新手填（这是"添加引擎"真正的价值：接入新模型零改代码、零重复配置）。
    /// 内置卡的参数由 EngineCardStore 按 id 覆盖合并（现场填过一次就记住）。
    /// </summary>
    public class EngineCardDefinition
    {
        /// <summary>注册 id（EngineRegistry 键；自定义卡自动生成 custom-xxxxxxxx）。</summary>
        public string Id { get; set; } = "";

        /// <summary>卡片显示名（建议直接用模型名，如 "Qwen3-VL-8B"）。</summary>
        public string Name { get; set; } = "";

        /// <summary>许可证说明（卡片胶囊文字，如 "许可证：Apache 2.0 · 商用主力"）。</summary>
        public string LicenseNote { get; set; } = "";

        /// <summary>是否 Mock 演示引擎（无 HTTP 依赖；卡片"当前演示"语义）。</summary>
        public bool IsMock { get; set; }

        /// <summary>是否内置卡（内置卡不可删除，只允许覆盖连接参数）。</summary>
        public bool IsBuiltin { get; set; }

        // —— 该卡独立的连接参数（切换时回填 EngineConfig / 推理参数表单） ——

        /// <summary>推理协议：openai / custom / ollama / anthropic（下拉四档，见落地指南 §2.3）。</summary>
        public string Protocol { get; set; } = "openai";

        /// <summary>HTTP 推理服务地址（不带端点路径，如 http://127.0.0.1:8000）。</summary>
        public string HttpBaseUrl { get; set; } = "";

        /// <summary>服务模型名（vLLM --served-model-name / Ollama tag / 自研服务路由名）。</summary>
        public string HttpModel { get; set; } = "";

        /// <summary>API Key（本地服务留空）。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>推理超时 ms（默认 5000：单帧数百 ms 量级，留足余量）。</summary>
        public int HttpTimeoutMs { get; set; } = 5000;

        /// <summary>深拷贝（内置卡输出给 UI 前复制一份，避免 UI 改动污染静态预设表）。</summary>
        public EngineCardDefinition Clone()
        {
            return new EngineCardDefinition
            {
                Id = Id,
                Name = Name,
                LicenseNote = LicenseNote,
                IsMock = IsMock,
                IsBuiltin = IsBuiltin,
                Protocol = Protocol,
                HttpBaseUrl = HttpBaseUrl,
                HttpModel = HttpModel,
                ApiKey = ApiKey,
                HttpTimeoutMs = HttpTimeoutMs,
            };
        }
    }
}
