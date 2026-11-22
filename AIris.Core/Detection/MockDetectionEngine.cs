using System;
using System.Collections.Generic;
using AIris.Core.Model;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AIris.Core.Detect
{
    /// <summary>
    /// Mock 检测引擎（AGENTS.md"Mock 演示三件套"之一）：
    /// 不做真推理，按 MockScene 在场零件直接返回归一化框——"即指即检"的演示替身。
    /// prompt 匹配规则：工步 ExpectedPart 里的类目关键词（去数量词后的短名，如"M3 盘头螺丝"）
    /// 与 MockScene 类目名做包含匹配；整检步（FinalCheck）无 prompt 时返回全部在场零件。
    /// 耗时模拟 ~30ms（0 也行，留一点真实感），统计口径与真引擎一致。
    /// </summary>
    public class MockDetectionEngine : IDetectionEngine
    {
        public string Id { get { return "mock"; } }
        public string Name { get { return "Mock 演示引擎"; } }
        public string LicenseNote { get { return "内置演示 · 无模型依赖"; } }
        public bool IsLoaded { get { return true; } }
        public EngineStats Stats { get; } = new EngineStats();

        public event Action<string> Log;

        public void Load(EngineConfig cfg)
        {
            // Mock 无需加载；清空场景给一个干净开局
            MockScene.Instance.Clear();
            OnLog("Mock 演示引擎就绪（无模型依赖，按 MockScene 出框）");
        }

        public Task<DetectResult> DetectAsync(Bitmap frame, IList<string> prompts, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var dets = new List<Detection>();
            try
            {
                // 整理待检类目：prompts 里的短名匹配 MockScene 类目；空 prompts = 整检全部
                var wanted = (prompts == null || prompts.Count == 0)
                    ? MockScene.PartLayout.Keys.ToList()
                    : prompts.Select(ExtractKeyword).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();

                foreach (var cat in wanted)
                {
                    foreach (var box in MockScene.Instance.BoxesOf(cat))
                    {
                        dets.Add(new Detection { Label = cat, X1 = box[0], Y1 = box[1], X2 = box[2], Y2 = box[3] });
                    }
                }
            }
            catch (Exception ex) { OnLog("Mock 检测异常：" + ex.Message); }

            sw.Stop();
            Stats.TotalCalls++;
            Stats.LastElapsedMs = sw.ElapsedMilliseconds;
            return Task.FromResult(new DetectResult { Ok = true, Detections = dets, ElapsedMs = sw.ElapsedMilliseconds });
        }

        /// <summary>
        /// 从自然语言 prompt 提取类目短名："4 颗 M3 盘头螺丝"→"M3 盘头螺丝"。
        /// 规则：剥离数量词（数字×N/N 颗/个/只 等）与装饰词，用剩余文本与 MockScene 类目做包含匹配。
        /// Mock 专用启发式，真实引擎不需要（服务端自己理解 prompt）。
        /// </summary>
        public static string ExtractKeyword(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "";
            var s = prompt.Trim();
            // 常见数量前缀：4 颗 / 2 个 / ×4 / x4 / 1 只
            s = System.Text.RegularExpressions.Regex.Replace(s, @"^\s*\d+\s*(颗|个|只|片|条|块)?\s*", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"^[×xX]\s*\d+\s*", "");
            // 找 MockScene 里第一个被 s 包含（或包含 s）的类目名，容忍语序差异
            foreach (var cat in MockScene.PartLayout.Keys)
            {
                if (s.Contains(cat) || cat.Contains(s)) return cat;
            }
            return s; // 未命中类目时原样返回（BoxesOf 自然返回空）
        }

        public void Dispose() { }

        private void OnLog(string msg)
        {
            var h = Log;
            if (h != null) h(msg);
        }
    }
}
