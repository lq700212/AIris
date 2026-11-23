using System;

namespace AIris.Core.Model
{
    /// <summary>
    /// 审计记录：每个工步判定变化落一条（对位设计稿 data-page="audit"）。
    /// 质量追溯链刚需（每步自动截图存档，客诉举证）。
    /// 持久化：Data\Audit\audit_yyyyMMdd.jsonl 每天一文件、一行一条；截图 PNG 同目录 Shots\ 下。
    /// </summary>
    public class AuditRecord
    {
        /// <summary>判定时刻。</summary>
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>产品 SN（演示态用时间戳生成，真机接读码器后替换）。</summary>
        public string Sn { get; set; } = "";

        /// <summary>配方名。</summary>
        public string RecipeName { get; set; } = "";

        /// <summary>工步序号（1 起）。</summary>
        public int StepIndex { get; set; }

        /// <summary>工步名。</summary>
        public string StepName { get; set; } = "";

        /// <summary>判定区域（ROI-A/全画面）。</summary>
        public string RoiId { get; set; } = "";

        /// <summary>判定：OK / NG。</summary>
        public string Verdict { get; set; } = "OK";

        /// <summary>NG 原因（OK 时为空，如"缺 1 颗：ROI-B 检出 3 / 预期 4"）。</summary>
        public string NgReason { get; set; } = "";

        /// <summary>判定依据快照（"数量=4 · IoU≥0.35 · 稳定3帧"，存档可复核判定依据）。</summary>
        public string RuleSnapshot { get; set; } = "";

        /// <summary>截图文件名（Shots\ 下的相对名；空=无截图）。</summary>
        public string ShotFile { get; set; } = "";

        /// <summary>引擎名（LocateAnything-3B / Qwen3-VL-8B / 自定义卡名 / Mock 演示）。</summary>
        public string EngineName { get; set; } = "";

        /// <summary>语义决策备注（V0.1.7 可选增强：NG 原因归纳，如"漏装"；默认空——
        /// 语义决策服务未启用时不写；旧记录无此字段反序列化为 null，JSON 兼容）。</summary>
        public string SemanticNote { get; set; } = "";
    }
}
