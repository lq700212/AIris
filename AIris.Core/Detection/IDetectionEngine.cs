using System;
using System.Collections.Generic;
using AIris.Core.Model;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace AIris.Core.Detect
{
    /// <summary>
    /// 单个检测框：开放词汇检测的输出（对位落地指南 §2.1——模型输出 0~1000 归一化坐标框，
    /// 文本形式 &lt;box&gt; x1,y1,x2,y2 &lt;/box&gt;）。AIris 全链路统一用这个归一化口径。
    /// ⚠️ 语义红线（AGENTS.md 铁律 11）：模型无置信度输出，本结构不携带分数字段；
    /// 位置判定只走 ROI-IoU 几何规则。
    /// </summary>
    public class Detection
    {
        /// <summary>框标签（引擎回填的类目/描述文本，如"M3 盘头螺丝"）。</summary>
        public string Label { get; set; } = "";

        /// <summary>归一化框 0~1000：左上/右下。</summary>
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }

        public override string ToString() { return Label + " (" + X1 + "," + Y1 + ")-(" + X2 + "," + Y2 + ")"; }
    }

    /// <summary>一次检测调用结果。</summary>
    public class DetectResult
    {
        public bool Ok { get; set; }
        /// <summary>失败原因（引擎未接入/超时/服务不可达等；Ok=false 时 UI 降级提示）。</summary>
        public string Error { get; set; } = "";
        public List<Detection> Detections { get; set; } = new List<Detection>();
        /// <summary>本帧推理耗时 ms（监控台/引擎页展示；Mock 约 0）。</summary>
        public long ElapsedMs { get; set; }
    }

    /// <summary>引擎运行统计（引擎页"服务状态"卡）。</summary>
    public class EngineStats
    {
        public long TotalCalls { get; set; }
        public long LastElapsedMs { get; set; }
        /// <summary>显存占用描述（Mock="—"；真引擎接入后回填，如"11.7 / 16 GB"）。</summary>
        public string MemoryNote { get; set; } = "—";
    }

    /// <summary>
    /// 检测引擎唯一接入面（AGENTS.md"大模型现状"红线：真实接入=实现本接口，禁止绕过）。
    ///
    /// 接入形态对位落地指南 §2.2：
    ///  - LocateAnything-3B / Qwen2.5-VL-7B 都是"Linux 常驻推理服务（FastAPI/vLLM）+ HTTP 调用"
    ///    部署形态 → 真实引擎 = 填实 HttpDetectionEngine 的 DetectAsync（组 JSON POST，
    ///    发图像 + prompts，回 0~1000 归一化框数组）。本接口签名即未来真实形态。
    ///  - POC 效果验证用 transformers 原生推理时，也可以包一层本地引擎实现本接口。
    ///
    /// 使用方（RunMonitor）只认本接口：采集帧 + 当前工步 prompts → 框列表。
    /// </summary>
    public interface IDetectionEngine : IDisposable
    {
        /// <summary>注册 id（EngineRegistry 键，如 mock / locateanything-http / qwen-vl-http）。</summary>
        string Id { get; }

        /// <summary>显示名（引擎卡标题，如"LocateAnything-3B"）。</summary>
        string Name { get; }

        /// <summary>许可证说明（引擎卡直接标注，避免误用——设计稿 engine 页"许可证：仅限研究"）。</summary>
        string LicenseNote { get; }

        /// <summary>是否已加载（加载成功后才可 Detect）。</summary>
        bool IsLoaded { get; }

        /// <summary>运行统计（引擎页只读展示）。</summary>
        EngineStats Stats { get; }

        /// <summary>加载/连接引擎（切换引擎确认弹窗后调用；失败抛异常由 VM 捕获提示）。</summary>
        void Load(EngineConfig cfg);

        /// <summary>
        /// 检测一帧。prompts = 自然语言检测目标（当前工步的 ExpectedPart 等）；
        /// 返回 0~1000 归一化框。服务不可达时 Ok=false + Error（不抛异常，调用方降级）。
        /// </summary>
        Task<DetectResult> DetectAsync(Bitmap frame, IList<string> prompts, CancellationToken ct);

        /// <summary>引擎内部日志出口（RunMonitor 转入运行日志）。</summary>
        event Action<string> Log;
    }
}
