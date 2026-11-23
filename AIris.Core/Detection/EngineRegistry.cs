using System;
using System.Collections.Generic;
using AIris.Core.Model;

namespace AIris.Core.Detect
{
    /// <summary>
    /// 引擎注册表：平台内置的引擎卡清单（设计稿 engine 页两张卡）+ 当前引擎管理。
    /// 真实引擎接入后在此追加一行即可（如 new HttpDetectionEngine("locateanything-http", ...)）。
    /// </summary>
    public static class EngineRegistry
    {
        /// <summary>内置引擎卡（id / 显示名 / 许可证说明）。顺序 = 引擎页卡序。</summary>
        public static readonly EngineCard[] Cards = new[]
        {
            // 演示引擎永远在列（Mock 三件套，AGENTS.md"Mock 演示三件套"）
            new EngineCard("mock", "Mock 演示引擎", "内置演示 · 无模型依赖", isMock: true),
            // LocateAnything-3B：POC 验证效果上限；许可证禁商用（落地指南 §2.1）
            new EngineCard("locateanything-http", "LocateAnything-3B", "许可证：仅限研究 · 不可商用（POC 专用）"),
            // Qwen2.5-VL-7B：商用主力（Apache 2.0；注意 3B 版同为 Research License）
            new EngineCard("qwen-vl-http", "Qwen2.5-VL-7B", "许可证：Apache 2.0 · 商用推荐"),
        };

        /// <summary>按 id 建引擎实例（每次新建，调用方负责 Dispose；Mock 单例语义在 MockScene）。
        /// 引擎卡隐含的默认推理协议（custom=自研 /detect 契约；openai=OpenAI 兼容）在此注入
        /// ——引擎页下拉可覆盖，落 INI 后以 INI 为准。</summary>
        public static IDetectionEngine Create(string engineId, EngineConfig cfg = null)
        {
            // 协议初值：仅在 cfg 未带协议（新建/空配置）时按卡默认注入，已配置则尊重配置
            if (cfg != null && string.IsNullOrEmpty(cfg.Protocol))
                cfg.Protocol = DefaultProtocol(engineId);
            switch (engineId)
            {
                case "mock": return new MockDetectionEngine();
                case "locateanything-http": return new HttpDetectionEngine("locateanything-http", "LocateAnything-3B", "许可证：仅限研究 · 不可商用（POC 专用）");
                case "qwen-vl-http": return new HttpDetectionEngine("qwen-vl-http", "Qwen2.5-VL-7B", "许可证：Apache 2.0 · 商用推荐");
                default: return new MockDetectionEngine();
            }
        }

        /// <summary>引擎卡默认推理协议（locateanything 走社区 batch 服务自研契约；qwen-vl 走 vLLM 的 OpenAI 兼容口）。</summary>
        public static string DefaultProtocol(string engineId)
        {
            return engineId == "qwen-vl-http" ? "openai" : "custom";
        }

        /// <summary>引擎卡定义。</summary>
        public class EngineCard
        {
            public string Id { get; private set; }
            public string Name { get; private set; }
            public string LicenseNote { get; private set; }
            /// <summary>是否 Mock（引擎页 UI 对 Mock 卡显示"当前演示"而非"切换"）。</summary>
            public bool IsMock { get; private set; }

            public EngineCard(string id, string name, string license, bool isMock = false)
            {
                Id = id; Name = name; LicenseNote = license; IsMock = isMock;
            }
        }
    }
}
