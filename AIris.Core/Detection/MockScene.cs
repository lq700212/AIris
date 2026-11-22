using System;
using System.Collections.Generic;
using System.Linq;

namespace AIris.Core.Detect
{
    /// <summary>
    /// Mock 演示场景（单例）：零件在场的唯一事实源。
    /// MockCameraSource 按它合成画面、MockDetectionEngine 按它出框——两件套永不同步漂移。
    /// 演示控制（运行监控台控制条 / 新建配方预填）通过本类增删零件，全链路自动联动。
    ///
    /// 零件类目即配方 Parts 的自然语言短名（如"金属垫板""M3 盘头螺丝"）；
    /// 每个类目的画面布局见 <see cref="PartLayout"/>（0~1000 归一化框位，演示配方"电动车控制器 V2.1"专用）。
    /// </summary>
    public sealed class MockScene
    {
        private static readonly MockScene _inst = new MockScene();
        public static MockScene Instance { get { return _inst; } }
        private MockScene() { }

        private readonly object _sync = new object();
        /// <summary>在场零件：类目名 → 在场数量（"M3 盘头螺丝"=3 表示 4 颗装了 3 颗）。</summary>
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>零件布局表：类目名 → 该类目每个实体的归一化框位（0~1000）。
        /// 与 MockCameraSource 的绘制位置必须一致（绘制和出框同源）。</summary>
        public static readonly Dictionary<string, List<int[]>> PartLayout =
            new Dictionary<string, List<int[]>>(StringComparer.OrdinalIgnoreCase)
        {
            // 每个实体框 [x1,y1,x2,y2]；顺序即 MockCameraSource 的绘制顺序
            { "金属垫板",     new List<int[]> { new[]{  90, 140, 460, 500 } } },
            { "散热器",       new List<int[]> { new[]{ 480, 280, 660, 500 } } },
            { "M3 盘头螺丝",  new List<int[]> { new[]{ 690, 300, 750, 360 }, new[]{ 800, 300, 860, 360 },
                                                 new[]{ 690, 400, 750, 460 }, new[]{ 800, 400, 860, 460 } } },
            { "电容模块",     new List<int[]> { new[]{ 150, 590, 350, 800 } } },
            { "端子排",       new List<int[]> { new[]{ 760, 600, 930, 680 }, new[]{ 760, 720, 930, 800 } } },
            { "盖板",         new List<int[]> { new[]{  60,  80, 950, 880 } } },
        };

        /// <summary>场景变化事件（监控台画面/统计刷新用）。</summary>
        public event Action Changed;

        /// <summary>类目在场数量（0 = 不在场）。</summary>
        public int CountOf(string part)
        {
            lock (_sync) { int v; return _counts.TryGetValue(part ?? "", out v) ? v : 0; }
        }

        /// <summary>类目在场的实体框位列表（按在场数量截取布局表前 N 个）。</summary>
        public List<int[]> BoxesOf(string part)
        {
            List<int[]> layout;
            if (part == null || !PartLayout.TryGetValue(part, out layout)) return new List<int[]>();
            lock (_sync) { int v; int n = _counts.TryGetValue(part, out v) ? v : 0; return layout.Take(Math.Min(n, layout.Count)).ToList(); }
        }

        /// <summary>放上一个零件（数量+1，封顶布局容量）。返回是否生效。</summary>
        public bool PlacePart(string part)
        {
            if (string.IsNullOrEmpty(part)) return false;
            List<int[]> layout;
            if (!PartLayout.TryGetValue(part, out layout)) return false;
            lock (_sync)
            {
                int v; _counts.TryGetValue(part, out v);
                if (v >= layout.Count) return false;
                _counts[part] = v + 1;
            }
            RaiseChanged();
            return true;
        }

        /// <summary>拿走一个零件（数量-1）。</summary>
        public bool RemovePart(string part)
        {
            lock (_sync)
            {
                int v; if (!_counts.TryGetValue(part ?? "", out v) || v <= 0) return false;
                _counts[part] = v - 1;
            }
            RaiseChanged();
            return true;
        }

        /// <summary>清空场景（新套装配开始/配方切换时）。</summary>
        public void Clear() { lock (_sync) { _counts.Clear(); } RaiseChanged(); }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}
