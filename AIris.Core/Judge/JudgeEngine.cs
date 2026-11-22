using System;
using System.Collections.Generic;
using System.Linq;
using AIris.Core.Detect;
using AIris.Core.Model;

namespace AIris.Core.Judge
{
    /// <summary>单次判定结论（RunMonitor 用于稳定帧累计与 NG 提示）。</summary>
    public class JudgeOutcome
    {
        public bool Pass { get; set; }
        /// <summary>本帧 ROI 内匹配到的数量。</summary>
        public int MatchedCount { get; set; }
        /// <summary>预期数量（规则里的；整检步为类目数）。</summary>
        public int ExpectedCount { get; set; }
        /// <summary>一句话结论（NG 原因/OK 描述，审计 NgReason 直接用）。</summary>
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// 判定引擎：纯几何/计数规则，确定性可审计（调研报告 §6.2——
    /// "位置判定用几何规则（检测框与预配置 ROI 的 IoU），不用模型'理解'位置"）。
    /// 无状态：每帧独立判定；"连续 N 帧稳定"由 WorkstepEngine/RunMonitor 在外层累计。
    /// </summary>
    public static class JudgeEngine
    {
        /// <summary>
        /// 判定一个工步的本帧结论。
        /// </summary>
        /// <param name="step">当前工步（决定预期语义：出现/消失/整检）。</param>
        /// <param name="roi">判定区域（null = 全画面）。</param>
        /// <param name="dets">本帧全部检测框（0~1000 归一化）。</param>
        /// <param name="recipeParts">整检步用的零件类目清单（来自配方 Parts）。</param>
        public static JudgeOutcome Judge(RecipeStep step, ROIRect roi, IList<Detection> dets, IList<string> recipeParts)
        {
            var rule = step.Rule;
            int matched = CountMatched(step, roi, dets, recipeParts);
            int expected = ExpectedCountOf(step, rule, recipeParts);

            bool pass;
            string detail;

            switch (step.Expectation)
            {
                case StepExpectation.Disappear:
                    // 消失语义：ROI 内应清空（0 个）
                    pass = matched == 0;
                    detail = pass ? "ROI 已清空" : "ROI 内仍有 " + matched + " 个未移除";
                    break;

                case StepExpectation.FinalCheck:
                    // 整检语义：全部零件类目齐套（matched = 类目命中数）
                    pass = matched >= expected;
                    detail = pass
                        ? "齐套 " + matched + "/" + expected + " 类"
                        : "缺 " + (expected - matched) + " 类（" + MissingParts(step, roi, dets, recipeParts) + "）";
                    break;

                default: // Appear
                    pass = rule.ExpectedCount.HasValue
                        ? matched == rule.ExpectedCount.Value
                        : matched >= 1;
                    detail = pass
                        ? "检出 " + matched + " / 预期 " + expected
                        : (matched < (rule.ExpectedCount ?? 1)
                            ? "缺 " + ((rule.ExpectedCount ?? 1) - matched) + "：ROI 检出 " + matched + " / 预期 " + (rule.ExpectedCount ?? 1)
                            : "多出 " + (matched - (rule.ExpectedCount ?? 1)) + "：ROI 检出 " + matched + " / 预期 " + (rule.ExpectedCount ?? 1));
                    break;
            }

            return new JudgeOutcome { Pass = pass, MatchedCount = matched, ExpectedCount = expected, Detail = detail };
        }

        /// <summary>ROI 内与工步预期匹配的框数（IoU ≥ 阈值才计入；整检步 = 命中的类目数）。</summary>
        private static int CountMatched(RecipeStep step, ROIRect roi, IList<Detection> dets, IList<string> recipeParts)
        {
            if (dets == null || dets.Count == 0) return 0;

            if (step.Expectation == StepExpectation.FinalCheck)
            {
                // 整检：每个配方类目至少有一个框落在全画面（忽略 ROI 约束，密集整检）即命中
                int hit = 0;
                foreach (var part in recipeParts ?? new List<string>())
                {
                    if (dets.Any(d => d != null && !string.IsNullOrEmpty(d.Label) &&
                        (d.Label.Contains(part) || part.Contains(d.Label)) && InRoi(d, null))) hit++;
                }
                return hit;
            }

            // 常规步：预期零件的关键词与框标签匹配 + IoU 判定在 ROI 内
            string keyword = MockDetectionEngine.ExtractKeyword(step.ExpectedPart);
            int n = 0;
            foreach (var d in dets)
            {
                if (d == null || string.IsNullOrEmpty(d.Label)) continue;
                bool labelMatch = string.IsNullOrEmpty(keyword) || d.Label.Contains(keyword) || keyword.Contains(d.Label);
                if (!labelMatch) continue;
                if (IoU(d, roi) >= step.Rule.IoUThreshold) n++;
            }
            return n;
        }

        /// <summary>预期数量：常规步取规则；消失步恒 0；整检步 = 类目数。</summary>
        private static int ExpectedCountOf(RecipeStep step, StepRule rule, IList<string> recipeParts)
        {
            if (step.Expectation == StepExpectation.Disappear) return 0;
            if (step.Expectation == StepExpectation.FinalCheck) return recipeParts == null ? 0 : recipeParts.Count;
            return rule.ExpectedCount ?? 1;
        }

        /// <summary>整检缺失类目名（NG 详情用）。</summary>
        private static string MissingParts(RecipeStep step, ROIRect roi, IList<Detection> dets, IList<string> recipeParts)
        {
            if (recipeParts == null) return "";
            var missing = new List<string>();
            foreach (var part in recipeParts)
            {
                bool found = dets != null && dets.Any(d => d != null && !string.IsNullOrEmpty(d.Label) &&
                    (d.Label.Contains(part) || part.Contains(d.Label)));
                if (!found) missing.Add(part);
            }
            return string.Join("、", missing.Take(3)) + (missing.Count > 3 ? " 等" : "");
        }

        /// <summary>检测框与 ROI 的交并比（0~1000 归一化同口径直接算；roi=null 视为全画面=恒过）。</summary>
        public static double IoU(Detection d, ROIRect roi)
        {
            if (roi == null) return 1.0;
            int ax1 = Math.Min(d.X1, d.X2), ay1 = Math.Min(d.Y1, d.Y2);
            int ax2 = Math.Max(d.X1, d.X2), ay2 = Math.Max(d.Y1, d.Y2);
            int bx1 = roi.X, by1 = roi.Y, bx2 = roi.X + roi.W, by2 = roi.Y + roi.H;
            int ix = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(ax1, bx1));
            int iy = Math.Max(0, Math.Min(ay2, by2) - Math.Max(ay1, by1));
            int inter = ix * iy;
            int union = (ax2 - ax1) * (ay2 - ay1) + (bx2 - bx1) * (by2 - by1) - inter;
            return union <= 0 ? 0 : (double)inter / union;
        }

        /// <summary>框是否落在区域内（整检全画面用）。</summary>
        private static bool InRoi(Detection d, ROIRect roi)
        {
            if (roi == null) return true;
            int cx = (d.X1 + d.X2) / 2, cy = (d.Y1 + d.Y2) / 2;
            return cx >= roi.X && cx <= roi.X + roi.W && cy >= roi.Y && cy <= roi.Y + roi.H;
        }
    }
}
