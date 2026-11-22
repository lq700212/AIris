using System;
using System.Windows.Media.Imaging;

namespace AIris.WPF.Views
{
    /// <summary>
    /// FrameRect —— Uniform letterbox 渲染区换算（V0.1.7 抽共用，监控台/编辑页同源）：
    /// 帧图在取像舞台内的实际显示矩形（内接 + 居中）。旧监控台硬编码 4:3（Mock 帧
    /// 固定 1280×960 才成立），真机相机换分辨率即错位——改按帧实际像素宽高换算，
    /// 任何分辨率零改动。纯函数静态类，无 UI 框架依赖（对标库 ZoomMapping 口径，
    /// 该类在 Kaleidoscope.Detection 工程，宿主不引重库故本地落一份）。
    /// </summary>
    public static class FrameRect
    {
        /// <summary>求 Uniform 拉伸下帧图的显示矩形（舞台像素坐标，含居中偏移）。
        /// 帧空/尺寸无效 → 回退整舞台矩形（无底图时坐标系即舞台本身）。</summary>
        public static void TryGet(BitmapSource frame, double stageW, double stageH,
                                  out double ox, out double oy, out double rw, out double rh)
        {
            if (stageW < 1 || stageH < 1) { ox = oy = rw = rh = 0; return; }
            if (frame != null && frame.PixelWidth > 0 && frame.PixelHeight > 0)
            {
                double scale = Math.Min(stageW / frame.PixelWidth, stageH / frame.PixelHeight);
                rw = frame.PixelWidth * scale;
                rh = frame.PixelHeight * scale;
            }
            else { rw = stageW; rh = stageH; }
            ox = (stageW - rw) / 2.0;
            oy = (stageH - rh) / 2.0;
        }
    }
}
