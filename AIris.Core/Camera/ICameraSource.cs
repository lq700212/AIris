using System;
using System.Drawing;
using System.Threading;

namespace AIris.Core.Camera
{
    /// <summary>相机中性帧（AIris 内部统一帧格式：System.Drawing.Bitmap，采集线程产出）。</summary>
    public class CameraFrame : IDisposable
    {
        public Bitmap Image { get; private set; }
        public DateTime Timestamp { get; private set; }
        /// <summary>场景无内容（Mock 空场；真机恒 false）——UI 据此回无帧空态。</summary>
        public bool IsEmptyScene { get; private set; }

        public CameraFrame(Bitmap image, bool emptyScene = false)
        {
            Image = image;
            Timestamp = DateTime.Now;
            IsEmptyScene = emptyScene;
        }

        public void Dispose()
        {
            if (Image != null) { Image.Dispose(); Image = null; }
        }
    }

    /// <summary>
    /// 相机源唯一接入面（AGENTS.md"Mock 演示三件套"配套：真机接入=实现本接口）。
    /// 接入指引（填实者读这里）：真机推荐直接包 Kaleidoscope 主库 `Cameras/` 的海康/大恒适配器
    /// （KxCamFrame 中性帧 → 转 CameraFrame），或者自己包 MvCameraControl.Net SDK：
    /// Start 里开采集流，回调里 Bitmap.FromHBitmap/像素拷贝转 Bitmap，触发 FrameArrived。
    /// RunMonitor 只认本接口，Mock/真机切换在 Services 层装配处（MockMode 开关）。
    /// </summary>
    public interface ICameraSource : IDisposable
    {
        string Name { get; }
        bool IsConnected { get; }

        /// <summary>帧到达（采集线程回调；消费方注意跨线程与帧生命周期——帧归消费方 Dispose）。</summary>
        event Action<CameraFrame> FrameArrived;

        /// <summary>相机事件日志（连接/断线/异常）。</summary>
        event Action<string> Log;

        void Start();
        void Stop();
    }
}
