using System;

namespace AIris.Core.Camera
{
    /// <summary>
    /// 海康相机源（**桩**，真机接入填实点——AGENTS.md"相机接入"）。
    ///
    /// 接入指引（填实者读这里）：推荐直接包 Kaleidoscope 主库 `Cameras/` 的海康适配器
    /// （KxCamFrame 中性帧转 CameraFrame），或自包 MvCameraControl.Net SDK：
    /// ① MV_CC_CreateDevice + MV_CC_OpenDevice（IP 配置来自 DeviceConfig.CameraIp）
    /// ② MV_CC_StartGrabbing，注册回调 → 像素转 Bitmap → FrameArrived
    /// ③ Stop 里 MV_CC_StopGrabbing + MV_CC_CloseDevice + MV_CC_DestroyDevice
    /// 当前 Start 即报"未接入"，不抛异常（RunMonitor 降级提示，UI 不崩）。
    /// </summary>
    public class HikCameraSourceStub : ICameraSource
    {
        public string Name { get { return "GigE 相机 · 海康（未接入）"; } }
        public bool IsConnected { get; private set; }

        public event Action<CameraFrame> FrameArrived;
        public event Action<string> Log;

        public void Start()
        {
            IsConnected = false;
            OnLog("海康相机源为桩实现：真机接入请实现 ICameraSource（见文件头指引），当前不产帧");
        }

        public void Stop() { IsConnected = false; }

        public void Dispose() { Stop(); }

        private void OnLog(string msg) { var h = Log; if (h != null) h(msg); }
    }
}
