using System;
using System.Collections.Generic;

namespace AIris.Core.Model
{
    /// <summary>
    /// 配方 = 一个产品/场景的全部检测定义（对位调研报告 §6"配方中心"，设计稿 data-page="recipe/editor"）。
    /// 核心思想：产品差异全部收敛在配方文件里，换产品零代码——零件类目用自然语言描述（引擎即指即检的
    /// prompt 来源），判定规则全部配置化（几何判定、确定性、可审计，调研报告 §6.2）。
    ///
    /// 持久化：Data\Recipes\&lt;Name&gt;.json（Newtonsoft 序列化，字段即本类公共属性）。
    /// 示例 JSON：
    /// {
    ///   "Name": "电动车控制器 V2.1",
    ///   "Description": "控制器总装防错",
    ///   "Enabled": true,
    ///   "Parts": ["金属垫板", "M3 盘头螺丝", "散热器", "电容模块", "端子排", "盖板"],
    ///   "Steps": [{
    ///     "Name": "安装散热器螺丝 ×4", "ExpectedPart": "4 颗 M3 盘头螺丝",
    ///     "Expectation": "Appear", "RoiId": "ROI-B",
    ///     "Rule": { "IoUThreshold": 0.35, "StableFrames": 3, "ExpectedCount": 4 }
    ///   }]
    /// }
    /// </summary>
    public class Recipe
    {
        /// <summary>配方唯一 id（GUID，重命名不改 id）。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>配方名（=文件名，如"电动车控制器 V2.1"）。</summary>
        public string Name { get; set; } = "";

        /// <summary>产品一句话描述（新建向导第 1 步录入）。</summary>
        public string Description { get; set; } = "";

        /// <summary>是否启用（全局最多启用一份；启用即运行监控台的判定依据）。</summary>
        public bool Enabled { get; set; }

        /// <summary>零件类目自然语言清单（引擎 prompt 的基础，如"4 颗 M3 盘头螺丝"）。</summary>
        public List<string> Parts { get; set; } = new List<string>();

        /// <summary>工步序列（顺序即装配顺序）。</summary>
        public List<RecipeStep> Steps { get; set; } = new List<RecipeStep>();

        /// <summary>判定区域清单（ROI-IoU 几何判定用；编辑器标画/数值编辑落这里）。</summary>
        public List<ROIRect> Rois { get; set; } = new List<ROIRect>();

        /// <summary>最近编辑时间（配方卡展示）。</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>工步数量（配方卡一行摘要）。</summary>
        public int StepCount { get { return Steps == null ? 0 : Steps.Count; } }

        /// <summary>ROI 汇总描述（配方卡一行摘要，如"ROI 6 个"）。</summary>
        public int RoiCount
        {
            get
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Steps != null) foreach (var s in Steps) if (!string.IsNullOrEmpty(s.RoiId)) ids.Add(s.RoiId);
                return ids.Count;
            }
        }
    }

    /// <summary>预期变化语义（调研报告 §6："零件X出现在ROI-A / 零件Y从ROI-B消失"）。</summary>
    public enum StepExpectation
    {
        /// <summary>出现：零件在 ROI 内检测到预期数量（装配到位）。</summary>
        Appear = 0,
        /// <summary>消失：零件从 ROI 内移除（如取走料件/拆除保护盖）。</summary>
        Disappear = 1,
        /// <summary>整检：全部零件类目齐套（最后一道全画面密集检测）。</summary>
        FinalCheck = 2
    }

    /// <summary>单个工步定义（引导屏文案 + 预期零件 + 判定区域 + 规则）。</summary>
    public class RecipeStep
    {
        /// <summary>工步名（引导屏文案，同步自配方卡/编辑器，如"安装散热器螺丝 ×4"）。</summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 预期零件自然语言描述——本平台的核心字段：改一句话就换检测目标，零训练
        /// （对位设计稿 editor 页"预期零件（自然语言）"输入框）。
        /// </summary>
        public string ExpectedPart { get; set; } = "";

        /// <summary>预期变化：出现/消失/整检。</summary>
        public StepExpectation Expectation { get; set; } = StepExpectation.Appear;

        /// <summary>判定区域 id（引用 ROIRect.Id；空 = 全画面）。</summary>
        public string RoiId { get; set; } = "";

        /// <summary>判定规则（IoU/稳定帧/数量）。</summary>
        public StepRule Rule { get; set; } = new StepRule();
    }

    /// <summary>
    /// 工步判定规则（全部确定性几何/计数规则，不依赖模型"理解"位置——调研报告 §6.2 第 2 条）。
    /// </summary>
    public class StepRule
    {
        /// <summary>检测框与 ROI 的 IoU 阈值（默认 0.35，对位设计稿 editor 页"IoU 阈值 0.35"）。</summary>
        public double IoUThreshold { get; set; } = 0.35;

        /// <summary>连续稳定帧数（默认 3：连续 3 帧同结论才判定，抗手部遮挡抖动）。</summary>
        public int StableFrames { get; set; } = 3;

        /// <summary>预期数量（null = 只判有无不校验数量；如螺丝 ×4 = 4）。</summary>
        public int? ExpectedCount { get; set; }

        /// <summary>安全件双通道（开放词汇检测 + 传统模板匹配，两者一致才放行；调研报告 §6.2 第 3 条）。
        /// 模板匹配通道尚未接入，此开关现阶段只落配置与审计口径（true 时审计记录标注"双通道"）。</summary>
        public bool DualChannel { get; set; }
    }

    /// <summary>
    /// 判定区域矩形。坐标口径统一 0~1000 归一化整数（与 LocateAnything 输出口径一致，
    /// 调研报告 §2.1："坐标为 0~1000 归一化整数"），换相机分辨率不用改配方。
    /// </summary>
    public class ROIRect
    {
        public string Id { get; set; } = "";

        /// <summary>区域名（如"ROI-B · 螺丝孔位区"）。</summary>
        public string Name { get; set; } = "";

        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }
}
