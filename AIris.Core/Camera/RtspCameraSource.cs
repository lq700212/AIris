using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AIris.Core.Model;
using Kaleidoscope.Threading;
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

        /// <summary>连接/停止互斥（后台连接线程与 Stop/Dispose 可能并发）。</summary>
        private readonly object _sync = new object();

        /// <summary>后台连接进行中（防重入：Start 连点/热切换不会开出两条连接）。</summary>
        private bool _starting;

        /// <summary>相机显示名（设备页/监控台顶栏用）。</summary>
        public string Name { get { return _name; } }

        /// <summary>是否已连接（Start 成功即 true；断流重连由捕获器内部自愈，不改变该值）。</summary>
        public bool IsConnected { get { return _connected; } }

        /// <summary>实际拉流地址（调试/日志用）。</summary>
        public string Url { get { return _url; } }

        /// <summary>
        /// 帧率上限（0 = 不限，默认 0）。
        ///
        /// 为什么需要：相机实际输出频率可能远高于业务需要——安格华 2592×1944 实测
        /// 回调 20fps（配置写 8fps 不代表实际输出），而全量消费时 MatToBitmap 约
        /// 3.5ms + UI 推帧回调约 14.5ms/帧，20fps 下仅这条链路就占单核约 36%。
        /// 监控台显示 12fps 已足够流畅，限流后 CPU 直降约 40%。
        ///
        /// 限流位置在 Mat 转换<b>之前</b>：省的是整条"转换 + 推帧"成本，不是只省渲染。
        /// 由宿主按场景设置（ServiceHub 创建时设 12），不在本类硬编码。
        /// </summary>
        public int MaxFps { get; set; }

        /// <summary>
        /// 解码后立即下采样的目标宽度（0 = 不下采样，默认）。
        ///
        /// 【为什么放在捕获器而不是 UI 层（2026-11-28 延时优化）】
        /// 相机 5MP 主码流下，若把全尺寸图往下传，每帧要多做三次大图操作：
        /// Clone(9MB) + Mat→Bitmap(9MB) + 呈现面 box filter，合计 10~15ms。
        /// 这段开销把消费速率压到相机生产速率之下 → RTSP 缓冲永久积压 →
        /// 画面延时随运行时间固定增大（AIris 实测 789ms，消费 10.1fps vs 生产 20fps）。
        /// 在解码输出处一次性 resize（INTER_AREA 2~3ms）后，整条链路只处理小图。
        ///
        /// 【口径】设置后 CameraFrame.Image 与信息行分辨率都按<b>下采样后</b>尺寸报告；
        /// 检测精度在 1600 宽下与全尺寸无实质差异（ROI-IoU 几何判定不依赖像素级细节）。
        /// 【时机】Start 前设置；运行期改动只影响后续帧。
        /// </summary>
        public int MaxPixelWidth { get; set; }

        /// <summary>
        /// 下次允许接受帧的 Stopwatch 时间戳（MaxFps 时间片累加用；0 = 尚未初始化）。
        /// 每接受一帧就 += 1000/MaxFps，保证长期平均帧率精确等于 MaxFps，
        /// 不受"相机帧间隔与限流周期不对齐"影响（详见 OnFrameReady 内的算法注释）。
        /// </summary>
        private long _nextAcceptTimestamp;

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
        /// 打开 RTSP 流（**后台线程连接**，立即返回）并启动捕获。
        ///
        /// 【为什么后台连接（2026-11-28 启动卡顿整改）】
        /// cap.Start 内部要完成 RTSP OPTIONS/DESCRIBE/SETUP/PLAY 四步握手，相机不在线时
        /// 同步阻塞到 TCP 超时（现场 192.168.1.188 实测 20s+）。本方法由启动链
        /// App.OnStartup → ServiceHub.Monitor.Start 在 UI 线程调用——旧版整窗"鼠标转圈"。
        /// 现改后台线程：Start 立即返回，连接结果由日志 + IsConnected 体现
        /// （顶栏状态胶囊 1s 轮询自动更新）。
        ///
        /// 失败（地址错/网络不可达/相机未开流）不抛异常：置 IsConnected=false + 记日志，
        /// 监控台据此显示"相机离线"而不是崩溃。
        /// </summary>
        public void Start()
        {
            lock (_sync)
            {
                if (_capturer != null || _starting) return;   // 已连接 / 连接中，防重入
                _starting = true;
                _stopping = false;
            }

            var cap = new RtspFrameCapturer();
            // 解码后立即下采样（0=不下采样）：见 MaxPixelWidth 注释——这是消除
            // "消费速率低于相机生产速率 → 缓冲永久积压 → 固定大延时"的关键一环。
            cap.MaxPixelWidth = MaxPixelWidth;
            cap.FrameReady += OnFrameReady;

            // KxBackground.Run：统一后台出口（异常必达宿主日志；裸 Task 的异常会被静默吞）。
            KxBackground.Run("camera.rtsp.connect", () =>
            {
                bool ok;
                try
                {
                    ok = cap.Start(_url);
                }
                catch (Exception ex)
                {
                    cap.FrameReady -= OnFrameReady;
                    try { cap.Dispose(); } catch { }
                    lock (_sync) { _starting = false; _connected = false; }
                    OnLog("RTSP 相机启动异常：" + ex.Message);
                    return;
                }

                bool discard;
                lock (_sync)
                {
                    _starting = false;
                    // 连接期间已被 Stop（或启动失败）：本次结果作废并释放
                    discard = _stopping || !ok;
                    if (!discard)
                    {
                        _capturer = cap;
                        _connected = true;
                    }
                }

                if (discard)
                {
                    cap.FrameReady -= OnFrameReady;
                    try { cap.Stop(); } catch { }
                    try { cap.Dispose(); } catch { }
                    if (!ok && !_stopping)
                        OnLog("RTSP 相机连接失败：" + _url + "（检查 IP/端口/通道与相机 RTSP 开关）");
                    return;
                }

                OnLog("RTSP 相机已连接：" + _url);
            });
        }

        /// <summary>停止捕获并释放（有界等待，卡死线程交由捕获器内部已知泄漏策略处理）。</summary>
        public void Stop()
        {
            RtspFrameCapturer cap;
            lock (_sync)
            {
                _stopping = true;
                cap = _capturer;
                _capturer = null;
            }

            if (cap != null)
            {
                cap.FrameReady -= OnFrameReady;
                try { cap.Stop(); } catch { }
                try { cap.Dispose(); } catch { }
            }

            if (_connected) OnLog("RTSP 相机已断开：" + _url);
            _connected = false;
            // 注意：_starting 不在这里清——后台连接线程完成时会看到 _stopping=true
            // 自行释放并清标记；强行清会让新 Start 与在途连接撞车（两条连接互相覆盖）。
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

            // 帧率上限：相机实际输出可能远高于业务需要（安格华 5MP 实测 20fps），
            // 超出的帧在转换前丢弃——MatToBitmap + UI 推帧合计约 18ms/帧，限流直接省 CPU。
            //
            // 【算法：时间片累加，而不是"距上次 ≥ 1000/fps"（2026-11-28 修正）】
            // 旧写法在固定帧间隔输入下会系统性少收：20fps 输入（帧间隔 50ms）限流 12fps
            // 时，判据是"距上次接受 ≥ 83.3ms"→ 50ms 被拒、100ms 才收 → 实际只有 10fps，
            // 且节拍 100ms 一停一顿（AIris 实测信息行 9fps，误以为是解码积压，排查半天）。
            // 现在改成"下次允许接受的时刻"累加时间片：接受一帧就把 next += 1000/fps，
            // 于是 20fps 输入限流 12fps = 每 5 帧收 3 帧，长期平均精确 12fps。
            // 长时间没帧（断流重连）时 next 会落后很多，用"落后超过 2 个时间片就重置"
            // 防止恢复瞬间连收一串。
            int maxFps = MaxFps;
            if (maxFps > 0)
            {
                long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                long intervalTicks = System.Diagnostics.Stopwatch.Frequency / maxFps;
                if (intervalTicks < 1) intervalTicks = 1;

                if (_nextAcceptTimestamp == 0)
                {
                    _nextAcceptTimestamp = nowTicks;
                }

                if (nowTicks < _nextAcceptTimestamp)
                {
                    mat.Dispose();
                    return;
                }

                _nextAcceptTimestamp += intervalTicks;
                if (_nextAcceptTimestamp < nowTicks - intervalTicks * 2)
                {
                    _nextAcceptTimestamp = nowTicks + intervalTicks;
                }
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
