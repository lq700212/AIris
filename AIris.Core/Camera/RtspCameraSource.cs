using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AIris.Core.Model;
using OpenCvSharp;
using YoloDetection;

namespace AIris.Core.Camera
{
    /// <summary>
    /// RTSP 网络相机源（安格华等 IP 相机）。
    ///
    /// 职责：把 Kaleidoscope.Detection 的 <see cref="RtspFrameCapturer"/>（OpenCV VideoCapture
    /// 拉 RTSP 流，自带断流自愈看门狗）适配成 AIris 的 <see cref="ICameraSource"/>：
    ///   RTSP 帧（BGR8 Mat）→ System.Drawing.Bitmap → CameraFrame → FrameArrived 事件。
    ///
    /// 为什么走 RTSP 而不是海康 SDK：现场相机（如安格华 ANGEHUA）是标准 RTSP 网络相机，
    /// 没有海康 MVS SDK 依赖；YoloDetector 同款相机即用 rtsp://{ip}:554/stream{channel} 接入，
    /// 本类与该路径保持一致（URL 模板可在设备页配置）。
    ///
    /// 线程模型：Start 在调用线程同步打开流（失败立即返回并报日志）；帧事件在捕获线程触发，
    /// 消费方（RunMonitor）自行处理跨线程。Mat 所有权按 RtspFrameCapturer 契约归订阅者，
    /// 本类转换完 Bitmap 后立即 Dispose Mat，绝不让 native 内存外泄。
    ///
    /// 断流自愈：完全委托 RtspFrameCapturer（连续失败重连 + 看门狗强杀重建），
    /// 本类只透传连接日志与帧，不重复造轮子。
    /// </summary>
    public class RtspCameraSource : ICameraSource
    {
        private readonly string _url;
        private readonly string _name;

        private RtspFrameCapturer _capturer;
        private volatile bool _connected;
        private volatile bool _stopping;

        /// <summary>相机显示名（设备页/监控台顶栏用）。</summary>
        public string Name { get { return _name; } }

        /// <summary>是否已连接（Start 成功即 true；断流重连由捕获器内部自愈，不改变该值）。</summary>
        public bool IsConnected { get { return _connected; } }

        /// <summary>实际拉流地址（调试/日志用）。</summary>
        public string Url { get { return _url; } }

        /// <summary>帧到达（捕获线程触发；帧归消费方 Dispose）。</summary>
        public event Action<CameraFrame> FrameArrived;

        /// <summary>相机事件日志（连接/失败/异常）。</summary>
        public event Action<string> Log;

        /// <summary>
        /// 构造：从设备配置拼出 RTSP 地址。
        /// 占位符 {ip}/{port}/{channel} 与 YoloDetector 的 RtspUrlFormat 口径一致，
        /// 换品牌只改模板不改代码。
        /// </summary>
        public RtspCameraSource(DeviceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            _url = BuildUrl(cfg);
            _name = "RTSP 相机 · " + ExtractHost(_url);
        }

        /// <summary>
        /// 按设备配置拼 RTSP 地址（设置页"测试连接"与本类共用同一口径）。
        /// 端口非法/空 → 回落 554；模板空 → 回落安格华默认模板。
        /// </summary>
        public static string BuildUrl(DeviceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            string ip = string.IsNullOrWhiteSpace(cfg.CameraIp) ? "127.0.0.1" : cfg.CameraIp.Trim();

            int port;
            if (!int.TryParse(cfg.CameraPort, out port) || port <= 0) port = 554;

            string format = string.IsNullOrWhiteSpace(cfg.RtspUrlFormat)
                ? "rtsp://{ip}:{port}/stream{channel}"
                : cfg.RtspUrlFormat;

            return format
                .Replace("{ip}", ip)
                .Replace("{port}", port.ToString())
                .Replace("{channel}", cfg.RtspChannel.ToString());
        }

        /// <summary>从 rtsp://host:port/... 里取 host（显示名用，解析失败原样返回）。</summary>
        private static string ExtractHost(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host;
            }
            catch
            {
                return url;
            }
        }

        /// <summary>
        /// 打开 RTSP 流并启动后台捕获线程。
        /// 失败（地址错/网络不可达/相机未开流）不抛异常：置 IsConnected=false + 记日志，
        /// 监控台据此显示"相机离线"而不是崩溃。
        /// </summary>
        public void Start()
        {
            if (_capturer != null) return;
            _stopping = false;

            var cap = new RtspFrameCapturer();
            cap.FrameReady += OnFrameReady;

            bool ok;
            try
            {
                ok = cap.Start(_url);
            }
            catch (Exception ex)
            {
                cap.FrameReady -= OnFrameReady;
                try { cap.Dispose(); } catch { }
                _connected = false;
                OnLog("RTSP 相机启动异常：" + ex.Message);
                return;
            }

            if (!ok)
            {
                cap.FrameReady -= OnFrameReady;
                try { cap.Dispose(); } catch { }
                _connected = false;
                OnLog("RTSP 相机连接失败：" + _url + "（检查 IP/端口/通道与相机 RTSP 开关）");
                return;
            }

            _capturer = cap;
            _connected = true;
            OnLog("RTSP 相机已连接：" + _url);
        }

        /// <summary>停止捕获并释放（有界等待，卡死线程交由捕获器内部已知泄漏策略处理）。</summary>
        public void Stop()
        {
            _stopping = true;

            var cap = _capturer;
            _capturer = null;
            if (cap != null)
            {
                cap.FrameReady -= OnFrameReady;
                try { cap.Stop(); } catch { }
                try { cap.Dispose(); } catch { }
            }

            if (_connected) OnLog("RTSP 相机已断开：" + _url);
            _connected = false;
        }

        public void Dispose() { Stop(); }

        /// <summary>
        /// 捕获线程回调：Mat（BGR8）→ Bitmap → CameraFrame。
        /// Mat 按契约由本方法 Dispose（转换完成后立即释放，不让 native 内存堆积）。
        /// </summary>
        private void OnFrameReady(object sender, Mat mat)
        {
            if (mat == null) return;

            if (_stopping)
            {
                mat.Dispose();
                return;
            }

            Bitmap bmp = null;
            try
            {
                bmp = MatToBitmap(mat);
            }
            catch (Exception ex)
            {
                OnLog("RTSP 帧转换失败：" + ex.Message);
            }
            finally
            {
                mat.Dispose();
            }

            if (bmp == null) return;

            var h = FrameArrived;
            if (h != null)
            {
                try { h(new CameraFrame(bmp)); }
                catch { bmp.Dispose(); }
            }
            else
            {
                bmp.Dispose();
            }
        }

        /// <summary>
        /// BGR8 Mat → 24bpp Bitmap（逐行按 Stride 拷贝，兼容 OpenCV 行对齐填充）。
        /// 说明：RtspFrameCapturer 已把任意输入格式统一转成 CV_8UC3 BGR，
        /// 这里不再做色彩空间判断，避免双份转换开销。
        /// </summary>
        private static Bitmap MatToBitmap(Mat mat)
        {
            int w = mat.Cols;
            int h = mat.Rows;
            if (w <= 0 || h <= 0) return null;

            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                int rowBytes = w * 3;
                long step = mat.Step();
                IntPtr srcBase = mat.Data;

                if (step == rowBytes)
                {
                    // 连续内存（无行填充）：整块一次拷贝
                    CopyMemory(bd.Scan0, srcBase, new IntPtr((long)rowBytes * h));
                }
                else
                {
                    for (int y = 0; y < h; y++)
                    {
                        CopyMemory(
                            new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride),
                            new IntPtr(srcBase.ToInt64() + y * step),
                            new IntPtr(rowBytes));
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }

        /// <summary>整块内存拷贝（kernel32 RtlMoveMemory，等价 memmove）。</summary>
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        private void OnLog(string msg)
        {
            var h = Log;
            if (h != null) h(msg);
        }
    }
}
