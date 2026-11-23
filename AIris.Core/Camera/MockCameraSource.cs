using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using AIris.Core.Detect;

namespace AIris.Core.Camera
{
    /// <summary>
    /// Mock 相机（AGENTS.md"Mock 演示三件套"之二）：按 MockScene 在场零件 GDI+ 合成俯拍画面。
    /// 绘制位置与 MockScene.PartLayout 同源（0~1000 归一化 → 画布坐标），保证"画面上看得见的
    /// 零件 = 引擎出的框"（演示可信度的根基）。
    /// 8 fps 软节拍（装配秒级人工节拍绰绰有余，落地指南 §2.6），独立后台线程。
    /// </summary>
    public class MockCameraSource : ICameraSource
    {
        public string Name { get { return "Mock 相机 · 2592×1944 合成"; } }

        private readonly object _sync = new object();
        private Thread _thread;
        private volatile bool _stop;
        public bool IsConnected { get; private set; }

        public event Action<CameraFrame> FrameArrived;
        public event Action<string> Log;

        /// <summary>画布分辨率（模拟 500 万像素养拍口径的 4:3 帧率档）。</summary>
        private const int W = 1280, H = 960;

        public void Start()
        {
            lock (_sync)
            {
                if (_thread != null) return;
                _stop = false;
                IsConnected = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "MockCamera" };
                _thread.Start();
                OnLog("Mock 相机启动（合成画面，8 fps 软节拍）");
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _stop = true;
                IsConnected = false;
                _thread = null;
                OnLog("Mock 相机停止");
            }
        }

        private void Loop()
        {
            while (!_stop)
            {
                try
                {
                    var bmp = Compose();
                    var h = FrameArrived;
                    if (h != null) h(new CameraFrame(bmp, IsSceneEmpty()));
                }
                catch (Exception ex) { OnLog("Mock 合帧异常：" + ex.Message); }
                Thread.Sleep(125); // 8 fps
            }
        }

        /// <summary>场景是否无内容（空场）：所有类目在场数量 = 0（真源 = MockScene 单例
        /// _counts，经 CountOf 读；PartLayout 是静态布局表，Count 恒 >0 不能当在场判断）。
        /// UI 侧据此回无帧空态（扫描动效恢复）——空场透明帧不算"有画面"。</summary>
        private static bool IsSceneEmpty()
        {
            foreach (var part in MockScene.PartLayout.Keys)
            {
                if (MockScene.Instance.CountOf(part) > 0) return false;
            }
            return true;
        }

    /// <summary>合成一帧：透明底（V0.1.5 口径：台面装饰=舞台一层网格+扫描，Mock 帧只画
    /// 零件，对齐真实监控视频"画面不带装饰"的事实——设计稿 .cam-img 网格在舞台层不随帧走）。
    /// 零件按类目绘制（与 PartLayout 同源坐标）。</summary>
    private Bitmap Compose()
    {
        var bmp = new Bitmap(W, H);   // GDI+ 默认 32bppArgb，透明底保 alpha
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // 透明底 ClearType 会出脏边，改灰度抗锯齿

            // 按类目绘制在场零件（类目固定一组颜色/画法，演示辨识度）
            foreach (var cat in MockScene.PartLayout.Keys)
            {
                foreach (var box in MockScene.Instance.BoxesOf(cat))
                {
                    DrawPart(g, font, cat, box);
                }
            }
        }
        return bmp;
    }

        /// <summary>单零件绘制：归一化框 → 画布矩形，按类目画形。</summary>
        private static void DrawPart(Graphics g, Font font, string cat, int[] b)
        {
            var r = new Rectangle(Scale(b[0], W), Scale(b[1], H), Scale(b[2], W) - Scale(b[0], W), Scale(b[3], H) - Scale(b[1], H));

            switch (cat)
            {
                case "金属垫板":   // 大圆角矩形板 + 四安装孔
                    using (var p = new Pen(Color.FromArgb(150, 130, 120), 3f)) g.FillRectangle(Brushes.Gainsboro, r);
                    using (var p = new Pen(Color.FromArgb(120, 110, 100), 2f)) g.DrawRectangle(p, r);
                    foreach (var c in CornerCenters(r, 18))
                    using (var pen = new Pen(Color.FromArgb(90, 80, 75), 2f)) g.DrawEllipse(pen, c.X - 8, c.Y - 8, 16, 16);
                    break;
                case "散热器":     // 竖鳍片组
                    g.FillRectangle(Brushes.Silver, r);
                    using (var p = new Pen(Color.FromArgb(120, 120, 125), 1f))
                    { for (int x = r.X + 6; x < r.Right; x += 10) g.DrawLine(p, x, r.Y + 4, x, r.Bottom - 4); }
                    break;
                case "M3 盘头螺丝": // 圆头 + 十字槽
                    using (var b2 = new SolidBrush(Color.FromArgb(70, 70, 75))) g.FillEllipse(b2, r);
                    using (var p = new Pen(Color.FromArgb(200, 200, 205), 2f))
                    {
                        g.DrawLine(p, r.X + 6, r.Y + r.Height / 2, r.Right - 6, r.Y + r.Height / 2);
                        g.DrawLine(p, r.X + r.Width / 2, r.Y + 6, r.X + r.Width / 2, r.Bottom - 6);
                    }
                    break;
                case "电容模块":   // 两颗圆柱电容
                    g.FillRectangle(Brushes.WhiteSmoke, r);
                    int cw = r.Width / 4;
                    g.FillEllipse(Brushes.DarkSlateBlue, r.X + cw, r.Y + 12, cw * 2, cw * 2);
                    g.FillEllipse(Brushes.RoyalBlue, r.X + cw, r.Y + r.Height - cw * 2 - 12, cw * 2, cw * 2);
                    break;
                case "端子排":     // 长条分格
                    g.FillRectangle(Brushes.LightGreen, r);
                    using (var p = new Pen(Color.FromArgb(60, 110, 70), 2f))
                    { for (int x = r.X; x <= r.Right; x += r.Width / 5) g.DrawLine(p, x, r.Y, x, r.Bottom); }
                    break;
                case "盖板":       // 半透明大盖板（整检时最后放）
                    using (var b2 = new SolidBrush(Color.FromArgb(70, 200, 200, 210))) g.FillRectangle(b2, r);
                    using (var p = new Pen(Color.FromArgb(150, 110, 110, 120), 2f)) g.DrawRectangle(p, r);
                    break;
                default:
                    g.FillRectangle(Brushes.LightGray, r);
                    break;
            }

            // 零件标签
            using (var tb = new SolidBrush(Color.FromArgb(90, 60, 55, 50)))
                g.DrawString(cat, font, tb, r.X + 2, r.Y + 2);
        }

        private static Point[] CornerCenters(Rectangle r, int inset)
        {
            return new[]
            {
                new Point(r.X + inset, r.Y + inset), new Point(r.Right - inset, r.Y + inset),
                new Point(r.X + inset, r.Bottom - inset), new Point(r.Right - inset, r.Bottom - inset)
            };
        }

        /// <summary>0~1000 归一化 → 画布像素。</summary>
        private static int Scale(int v, int dim) { return Math.Max(0, Math.Min(dim, v * dim / 1000)); }

        public void Dispose() { Stop(); }

        private void OnLog(string msg)
        {
            var h = Log;
            if (h != null) h(msg);
        }
    }
}
