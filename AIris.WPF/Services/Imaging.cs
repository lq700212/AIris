using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AIris.WPF.Services
{
    /// <summary>
    /// GDI Bitmap → WPF BitmapSource 转换（跨线程安全：Freeze 后可在任何线程读）。
    /// HBitmap 必须配对 DeleteObject，否则 GDI 句柄泄漏（长时间运行必炸，监控台 8fps 高频路径）。
    /// </summary>
    public static class Imaging
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>转换并 Freeze（结果可跨线程绑定）；失败返回 null。</summary>
        public static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            if (bmp == null) return null;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = bmp.GetHbitmap();
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                if (h != IntPtr.Zero) DeleteObject(h);
            }
        }
    }
}
