using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIris.Core.Detect
{
    /// <summary>语义决策问题题型（对位 JEV Decisions 三原语；最小映射，具体协议在 Infer 层）。</summary>
    public enum SemQuestionType { Choice, Noul }

    /// <summary>一道语义决策问题（状态 + 问法 + 选项）。</summary>
    public class SemDecisionQuestion
    {
        public SemQuestionType Type { get; set; }
        /// <summary>问法（Choice：问什么；Noul：命题）。</summary>
        public string Instructions { get; set; } = "";
        /// <summary>Choice 选项（键→描述；Noul 忽略）。</summary>
        public Dictionary<string, string> Options { get; set; }
    }

    /// <summary>语义决策回包（带校准置信度——调用方按阈值决定采纳或忽略）。</summary>
    public class SemDecisionAnswer
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        /// <summary>Choice：选中键；Noul：null。</summary>
        public string Choice { get; set; }
        /// <summary>Noul：成立概率 0~1；Choice：置信度。</summary>
        public double Value { get; set; }
    }

    /// <summary>
    /// 语义决策扩展点（V0.1.7 JEV 调研结论：云端文本决策模型——无图像输入、闭源无本地权重，
    /// 与 AIris 全本地离线部署红线错位，**不进生产主链路**；本接口只是"未来若启用"的插槽，
    /// 唯一应用点是审计 NG 原因归纳增强——判定本身永远走确定性几何规则，语义模型只做
    /// "事后归档辅助"，不参与 OK/NG 结论）。
    /// 默认装配 NullSemanticDecisionService（恒"未启用"，零开销不联网）；启用 = INI 开关
    /// + 装配处换实现（DecisionsTransport 承载，Kaleidoscope.Infer）。
    /// </summary>
    public interface ISemanticDecisionService
    {
        /// <summary>是否启用（false 时调用方直接跳过，不该发起网络请求）。</summary>
        bool Enabled { get; }

        /// <summary>发一道语义决策（失败 Ok=false 不抛——增强链路绝不打断主流程）。</summary>
        Task<SemDecisionAnswer> DecideAsync(string state, SemDecisionQuestion q, CancellationToken ct);
    }

    /// <summary>空实现（默认装配）：恒"未启用"，DecideAsync 不联网直接返回失败——零开销占位。</summary>
    public class NullSemanticDecisionService : ISemanticDecisionService
    {
        public bool Enabled { get { return false; } }

        public Task<SemDecisionAnswer> DecideAsync(string state, SemDecisionQuestion q, CancellationToken ct)
        {
            return Task.FromResult(new SemDecisionAnswer { Ok = false, Error = "语义决策未启用" });
        }
    }
}
