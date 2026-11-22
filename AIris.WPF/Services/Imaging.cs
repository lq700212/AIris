using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIris.WPF.Services
{
    /// <summary>
    /// GDI Bitmap → WPF BitmapSource 转换（跨线程安全：Freeze 后可在任何线程读）。
    /// 【V0.1.5 保 alpha】弃 GetHbitmap 路线（HBitmap 无 alpha 语义，透明像素被压成黑底，
    /// Mock 透明帧会糊死舞台网格）——改 LockBits 直拷像素到 Pbgra32（预乘 alpha，WPF
    /// 原生格式，零二次转换），8fps 高频路径也无 GDI 句柄开销（旧路线的 DeleteObject
    /// 配对纪律随之退役）。
    /// </summary>
    public static class Imaging
    {
        /// <summary>转换并 Freeze（结果可跨线程绑定）；失败返回 null。</summary>
        public static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            if (bmp == null) return null;
            try
            {
                var data = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    // 【alpha 语义红线】GDI Format32bppArgb = 直通 alpha（非预乘），目标格式必须
                    // 用 Bgra32（直通）——喂 Pbgra32（预乘）会把半透明像素当预乘值解读，显示时
                    // 叠底溢出爆白（V0.1.5 实测：透明帧零件区全糊成白）。字节序两者同为 B,G,R,A。
                    int bytes = data.Stride * data.Height;
                    var buffer = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
                    var src = BitmapSource.Create(
                        data.Width, data.Height, 96, 96,
                        PixelFormats.Bgra32, null, buffer, data.Stride);
                    src.Freeze();
                    return src;
                }
                finally { bmp.UnlockBits(data); }
            }
            catch { return null; }
        }
    }
}
