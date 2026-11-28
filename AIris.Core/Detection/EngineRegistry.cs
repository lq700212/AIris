using System;
using System.Collections.Generic;
using System.Linq;
using AIris.Core.Model;
using AIris.Core.Store;

namespace AIris.Core.Detect
{
    /// <summary>
    /// 引擎注册表：内置引擎卡预设（设计稿 engine 页卡列）+ 自定义卡合并 + 引擎实例工厂。
    ///
    /// 【卡从哪来】
    ///  - 内置卡：本类 Builtin 静态表（LocateAnything-3B / Qwen2.5-VL-7B / Qwen3-VL-8B /
    ///    Qwen2.5-VL-3B·Ollama）；
    ///  - 自定义卡：引擎页「＋ 添加引擎」→ EngineCardStore（Data\EngineCards.json）。
    ///  AllCards() 把两者合并，内置卡的连接参数用 store 里的现场覆盖值（按 id）回填。
    ///
    /// 【Mock 不是卡】2026-11-28 用户定版：Mock 从「引擎卡 + 相机类型」两处收口为全局
    /// 「Mock 数据模式」（[App] MockMode，顶栏按钮驱动，见 ServiceHub.SetMockMode）。
    /// 本表只列真实模型；Create("mock") 仅为旧 INI 兼容保留（返回 Mock 引擎，不生成卡片）。
    ///
    /// 【为什么要每卡独立参数】现场可能同时准备多套模型（vLLM 换 --served-model-name 即可切换），
    /// 切卡时协议/地址/模型名必须跟着卡走，不能共用一套表单值（换模型就白跑一趟手填）。
    ///
    /// 【接入新模型的正确姿势】优先在引擎页点「＋ 添加引擎」（零代码）；
    /// 只有需要新的协议适配/内置预设时才改本文件。
    /// </summary>
    public static class EngineRegistry
    {
        /// <summary>
        /// 内置引擎卡预设（顺序 = 引擎页卡序）。连接参数是"推荐默认值"，
        /// 现场可在引擎页改并「应用配置」——参数按 id 覆盖存 EngineCardStore。
        /// </summary>
        public static readonly EngineCardDefinition[] Builtin = new[]
        {
            // LocateAnything-3B：POC 验证效果上限；许可证禁商用（落地指南 §2.1）
            new EngineCardDefinition
            {
                Id = "locateanything-http", Name = "LocateAnything-3B",
                LicenseNote = "许可证：仅限研究 · 不可商用（POC 专用）",
                IsBuiltin = true, Protocol = "custom",
                HttpBaseUrl = "http://127.0.0.1:8000", HttpModel = "locateanything-3b",
            },
            // Qwen2.5-VL-7B：上一代商用卡（Apache 2.0；注意 3B 版同为 Research License）
            // 保留原因：BF16 约 14GB，不接受 FP8 量化的现场仍可用 16GB 卡原精度跑
            new EngineCardDefinition
            {
                Id = "qwen-vl-http", Name = "Qwen2.5-VL-7B",
                LicenseNote = "许可证：Apache 2.0 · 上一代对照",
                IsBuiltin = true, Protocol = "openai",
                HttpBaseUrl = "http://127.0.0.1:8000", HttpModel = "qwen2.5-vl-7b",
            },
            // Qwen3-VL-8B：当前商用主力（Apache 2.0；BF16 约 16GB，16GB 卡必须用官方 FP8 权重，
            // 见落地指南 §2.6/§2.7）。grounding/OCR/指令跟随代际强化，输出 JSON bbox_2d 与协议层原生兼容。
            new EngineCardDefinition
            {
                Id = "qwen3-vl-http", Name = "Qwen3-VL-8B",
                LicenseNote = "许可证：Apache 2.0 · 商用主力",
                IsBuiltin = true, Protocol = "openai",
                HttpBaseUrl = "http://127.0.0.1:8000", HttpModel = "Qwen/Qwen3-VL-8B-Instruct",
            },
            // Qwen2.5-VL-3B（Ollama 本地）：开发机/低配先验链路（落地指南 §2.9.5）。
            // 用途不是产线精度，而是"没有 GPU 服务、只有一台办公机"时先把取像→推理→判定→审计
            // 全链路跑通（GGUF 量化 3.2GB，CPU 可跑）。协议用 Ollama 原生 /api/chat（裸 base64 图像）。
            // 超时必须放大：2026-11-28 本机实测单帧 12~27s（冷启动含 11s 模型加载、热态约 12s），
            // 默认 5000ms 会每帧超时降级——现场误以为"连不上"。
            new EngineCardDefinition
            {
                Id = "qwen25vl-3b-ollama", Name = "Qwen2.5-VL-3B",
                LicenseNote = "许可证：Apache 2.0 · 本地 Ollama 先验",
                IsBuiltin = true, Protocol = "ollama",
                HttpBaseUrl = "http://127.0.0.1:11434", HttpModel = "qwen2.5vl:3b",
                HttpTimeoutMs = 120000,
            },
        };

        /// <summary>
        /// 全部引擎卡 = 内置卡（参数用 store 覆盖合并） + 自定义卡。
        /// store 为 null 时只返回内置默认（App 装配前的探针/单测场景）。
        /// </summary>
        public static List<EngineCardDefinition> AllCards(EngineCardStore store)
        {
            var list = new List<EngineCardDefinition>();
            foreach (var b in Builtin)
            {
                var card = b.Clone();
                var ov = store == null ? null : store.GetOverride(card.Id);
                if (ov != null)
                {
                    // 只覆盖连接参数；名称/许可证说明以内置定义为准（版本升级可统一改口径）
                    card.Protocol = string.IsNullOrEmpty(ov.Protocol) ? card.Protocol : ov.Protocol;
                    if (!string.IsNullOrEmpty(ov.HttpBaseUrl)) card.HttpBaseUrl = ov.HttpBaseUrl;
                    if (!string.IsNullOrEmpty(ov.HttpModel)) card.HttpModel = ov.HttpModel;
                    if (!string.IsNullOrEmpty(ov.ApiKey)) card.ApiKey = ov.ApiKey;
                    if (ov.HttpTimeoutMs > 0) card.HttpTimeoutMs = ov.HttpTimeoutMs;
                }
                list.Add(card);
            }
            if (store != null) list.AddRange(store.LoadCustom());
            return list;
        }

        /// <summary>按 id 找卡（内置优先，其次自定义；找不到返回 null）。</summary>
        public static EngineCardDefinition Find(string engineId, EngineCardStore store)
        {
            if (string.IsNullOrEmpty(engineId)) return null;
            return AllCards(store).FirstOrDefault(c =>
                string.Equals(c.Id, engineId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 按 id 建引擎实例（每次新建，调用方负责 Dispose；Mock 单例语义在 MockScene）。
        /// 内置卡/自定义卡统一走 HttpDetectionEngine——换模型 = 换卡，不改代码（落地指南 §2.2）。
        ///
        /// 兼容口径：旧 INI 可能仍写着 ActiveEngineId=mock（Mock 卡已删除）——这里仍返回
        /// Mock 引擎保证老现场启动不炸；正常演示请走全局 Mock 数据模式（ServiceHub.MockMode）。
        /// </summary>
        public static IDetectionEngine Create(string engineId, EngineConfig cfg = null, EngineCardStore store = null)
        {
            if (string.Equals(engineId, "mock", StringComparison.OrdinalIgnoreCase))
                return new MockDetectionEngine();

            var def = Find(engineId, store);
            if (def == null || def.IsMock) return new MockDetectionEngine();

            // 协议初值：仅在 cfg 未带协议（新建/空配置）时按卡默认注入，已配置则尊重配置
            if (cfg != null && string.IsNullOrEmpty(cfg.Protocol)) cfg.Protocol = def.Protocol;
            return new HttpDetectionEngine(def.Id, def.Name, def.LicenseNote);
        }

        /// <summary>引擎卡默认推理协议（找不到卡时按自研契约兜底）。</summary>
        public static string DefaultProtocol(string engineId, EngineCardStore store = null)
        {
            var def = Find(engineId, store);
            return def == null ? "custom" : def.Protocol;
        }
    }
}
