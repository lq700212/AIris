using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.ViewModels
{
    /// <summary>
    /// 设备与集成 VM（设计稿 data-page="settings"）：左设备列表（选中切换参数卡）+ 右参数表单。
    /// 参数落 MainSetting.ini（DeviceConfig）；Mock 态参数即配置真机时的目标值，保存即生效语义。
    /// </summary>
    public class SettingsViewModel : PageViewModelBase
    {
        public override string Id => "settings";
        public override string Title => "设备与集成";
        public override string Icon => "⚙";

        /// <summary>
        /// 设备项（左列表行）。
        /// V0.1.12：补 Status 连接状态（设计稿 vNext 左导航状态点）——
        /// 在线 = 绿（已连通）/ 离线 = 灰（连通但未响应）/ 未配置 = 橙（参数未填或未启用）。
        /// 状态值只做三档枚举文案，颜色由 XAML DataTrigger 映射到 Theme* 键（不写死色值）。
        /// 继承 ViewModelBase：保存参数后 RefreshStatus() 实时刷新状态点（不是硬编码假状态）。
        /// </summary>
        public class DeviceItem : ViewModelBase
        {
            public string Icon { get; set; }
            public string Name { get; set; }

            private string _status = "NotConfigured";
            /// <summary>连接状态：Online / Offline / NotConfigured（XAML 依此上色）。</summary>
            public string Status { get { return _status; } set { SetProperty(ref _status, value); } }

            private string _statusText = "未配置";
            /// <summary>状态中文说明（列表行 ToolTip）。</summary>
            public string StatusText { get { return _statusText; } set { SetProperty(ref _statusText, value); } }
        }

        /// <summary>
        /// 设备列表（左列；顺序即设计稿）。字段暴露为具名属性，RefreshStatus() 直接改状态点。
        /// 状态语义（vNext 三态）：Online 绿 / Offline 灰 / NotConfigured 橙。
        /// </summary>
        public DeviceItem Camera { get; } = new DeviceItem { Icon = "◉", Name = "相机 · Mock / RTSP / GigE" };
        public DeviceItem Light { get; } = new DeviceItem { Icon = "☀", Name = "光源控制器" };
        public DeviceItem Guide { get; } = new DeviceItem { Icon = "▢", Name = "引导屏" };
        public DeviceItem Beacon { get; } = new DeviceItem { Icon = "♪", Name = "声光报警灯" };
        public DeviceItem Mes { get; } = new DeviceItem { Icon = "⇄", Name = "MES · HTTP" };

        public DeviceItem[] Devices { get; }

        public SettingsViewModel()
        {
            Devices = new[] { Camera, Light, Guide, Beacon, Mes };
            SaveCommand = new RelayCommand(Save);
            TestCameraCommand = new RelayCommand(TestCamera);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
            RefreshStatus();
        }

        /// <summary>
        /// 刷新左导航状态点（Load/Save 后调用）。判定规则（Mock 演示态可解释、保存即见变化）：
        ///  · 相机：Mock 合成始终在线（与顶栏"相机在线"胶囊同口径）；真机接入后改判 SDK 连接态。
        ///  · 光源：亮度参数已给且漫射罩开 = 已配置但仍离机（Mock 无控制器）→ Offline；
        ///    亮度 &lt;= 0 视为未配置。
        ///  · 引导屏/声光：开关开 = 已配置（设备未接 → Offline）；关 = 未配置（橙）。
        ///  · MES：地址空 = 未配置（橙）；有地址且启用 = 在线（绿）；有地址未启用 = 离线（灰）。
        /// </summary>
        private void RefreshStatus()
        {
            // 相机状态按配置类型判定（与 ServiceHub.CreateCamera 装配口径一致）：
            //   mock = 演示合成始终在线（绿）；
            //   rtsp = 优先读运行中相机的真实连接态（保存热切换后立即变绿）；
            //   hik = SDK 桩未接入（橙）；IP 空 = 未配置（橙）。
            var live = ServiceHub.Monitor != null ? ServiceHub.Monitor.Camera : null;

            if (CameraKindIndex == 0)
            {
                Camera.Status = "Online";
                Camera.StatusText = "在线 · Mock 合成画面";
            }
            else if (CameraKindIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(CameraIp))
                {
                    Camera.Status = "NotConfigured";
                    Camera.StatusText = "未配置 · 未填写相机 IP";
                }
                else if (live is AIris.Core.Camera.RtspCameraSource && live.IsConnected)
                {
                    Camera.Status = "Online";
                    Camera.StatusText = "在线 · " + live.Name;
                }
                else
                {
                    Camera.Status = "Offline";
                    Camera.StatusText = "已配置 · 点「保存」立即应用";
                }
            }
            else
            {
                Camera.Status = "NotConfigured";
                Camera.StatusText = "未接入 · 海康 SDK 桩实现";
            }

            if (LightBrightness <= 0)
            {
                Light.Status = "NotConfigured";
                Light.StatusText = "未配置 · 亮度为 0";
            }
            else
            {
                Light.Status = "Offline";
                Light.StatusText = "离线 · 未接入控制器（参数已保存）";
            }

            Guide.Status = GuideScreenOn ? "Offline" : "NotConfigured";
            Guide.StatusText = GuideScreenOn ? "离线 · 已启用，未接入屏幕" : "未配置 · 未启用同步";

            Beacon.Status = BeaconOn ? "Offline" : "NotConfigured";
            Beacon.StatusText = BeaconOn ? "离线 · 已启用，未接入报警灯" : "未配置 · 未启用联动";

            if (string.IsNullOrWhiteSpace(MesUrl))
            {
                Mes.Status = "NotConfigured";
                Mes.StatusText = "未配置 · 未填写接口地址";
            }
            else if (MesEnabled)
            {
                Mes.Status = "Online";
                Mes.StatusText = "在线 · 已启用上报（Mock 不发包）";
            }
            else
            {
                Mes.Status = "Offline";
                Mes.StatusText = "离线 · 已填地址但未启用上报";
            }
        }

        private int _selectedDevice;
        /// <summary>选中设备索引（参数卡标题跟随；全参数始终可见——AIris 参数少，一屏放得下）。</summary>
        public int SelectedDevice
        {
            get { return _selectedDevice; }
            set { SetProperty(ref _selectedDevice, value); OnPropertyChanged(nameof(SelectedDeviceName)); }
        }

        public string SelectedDeviceName { get { return Devices[_selectedDevice].Name; } }

        // —— 参数字段（DeviceConfig 直载） ——
        private string _cameraIp = "";
        public string CameraIp { get { return _cameraIp; } set { SetProperty(ref _cameraIp, value); } }

        private string _cameraPort = "";
        public string CameraPort { get { return _cameraPort; } set { SetProperty(ref _cameraPort, value); } }

        /// <summary>
        /// 相机类型索引（ComboBox SelectedIndex 绑定）：
        /// 0 = Mock 合成画面 / 1 = RTSP 网络相机 / 2 = 海康 GigE 相机。
        /// 保存时映射回 DeviceConfig.CameraKind（mock/rtsp/hik），ServiceHub 按此装配相机源。
        /// </summary>
        private int _cameraKindIndex;
        public int CameraKindIndex
        {
            get { return _cameraKindIndex; }
            set { SetProperty(ref _cameraKindIndex, value); }
        }

        /// <summary>RTSP 通道号（安格华相机 stream{channel}，0 起）。</summary>
        private int _rtspChannel;
        public int RtspChannel
        {
            get { return _rtspChannel; }
            set { SetProperty(ref _rtspChannel, value); }
        }

        /// <summary>RTSP 地址模板（占位符 {ip}/{port}/{channel}；空=默认安格华模板）。</summary>
        private string _rtspUrlFormat = "rtsp://{ip}:{port}/stream{channel}";
        public string RtspUrlFormat
        {
            get { return _rtspUrlFormat; }
            set { SetProperty(ref _rtspUrlFormat, value); }
        }

        private int _exposureUs;
        public int ExposureUs { get { return _exposureUs; } set { SetProperty(ref _exposureUs, value); } }

        private int _lightBrightness = 72;
        public int LightBrightness { get { return _lightBrightness; } set { SetProperty(ref _lightBrightness, value); } }

        private bool _diffuserOn = true;
        public bool DiffuserOn { get { return _diffuserOn; } set { SetProperty(ref _diffuserOn, value); } }

        private bool _guideScreenOn = true;
        public bool GuideScreenOn { get { return _guideScreenOn; } set { SetProperty(ref _guideScreenOn, value); } }

        private bool _beaconOn = true;
        public bool BeaconOn { get { return _beaconOn; } set { SetProperty(ref _beaconOn, value); } }

        private string _mesUrl = "";
        public string MesUrl { get { return _mesUrl; } set { SetProperty(ref _mesUrl, value); } }

        private bool _mesEnabled;
        public bool MesEnabled { get { return _mesEnabled; } set { SetProperty(ref _mesEnabled, value); } }

        private string _cameraState = "已连接（Mock）";
        public string CameraState { get { return _cameraState; } private set { SetProperty(ref _cameraState, value); } }

        public ICommand SaveCommand { get; }
        public ICommand TestCameraCommand { get; }
        public ICommand BackCommand { get; }

        public override void OnEnter() { Load(); }

        private void Load()
        {
            var d = ServiceHub.Settings.ReadDevices();
            CameraKindIndex = KindToIndex(d.CameraKind);
            CameraIp = d.CameraIp;
            CameraPort = d.CameraPort;
            RtspChannel = d.RtspChannel;
            RtspUrlFormat = d.RtspUrlFormat;
            ExposureUs = d.CameraExposureUs;
            LightBrightness = d.LightBrightness;
            DiffuserOn = d.LightDiffuserOn;
            GuideScreenOn = d.GuideScreenOn;
            BeaconOn = d.BeaconOn;
            MesUrl = d.MesUrl;
            MesEnabled = d.MesEnabled;
            RefreshStatus();
        }

        private void Save()
        {
            var d = new DeviceConfig
            {
                CameraKind = IndexToKind(CameraKindIndex),
                CameraIp = CameraIp,
                CameraPort = CameraPort,
                RtspChannel = RtspChannel,
                RtspUrlFormat = RtspUrlFormat,
                CameraExposureUs = ExposureUs,
                LightBrightness = LightBrightness,
                LightDiffuserOn = DiffuserOn,
                GuideScreenOn = GuideScreenOn,
                BeaconOn = BeaconOn,
                MesUrl = MesUrl,
                MesEnabled = MesEnabled
            };
            ServiceHub.Settings.WriteDevices(d);
            // 相机参数保存后立即热切换（不用重启）：Mock ↔ RTSP ↔ GigE 随时切回
            ServiceHub.ApplyCamera();
            RefreshStatus();   // 左导航状态点跟随新参数（保存即见变化，不是假状态）
            ServiceHub.Host.Info("设备参数已保存。相机配置已立即应用（Mock ↔ RTSP 可随时切换）。", "保存成功");
        }

        /// <summary>相机类型索引 → DeviceConfig.CameraKind 值（mock/rtsp/hik）。</summary>
        private static string IndexToKind(int index)
        {
            switch (index)
            {
                case 1: return "rtsp";
                case 2: return "hik";
                default: return "mock";
            }
        }

        /// <summary>DeviceConfig.CameraKind 值 → ComboBox 索引。</summary>
        private static int KindToIndex(string kind)
        {
            if (string.Equals(kind, "rtsp", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(kind, "hik", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        /// <summary>
        /// 采集测试帧：Mock 直接确认；RTSP 后台 TCP 探测相机端口（不阻塞 UI 线程）；
        /// 海康 GigE 提示 SDK 桩未接入。
        /// </summary>
        private void TestCamera()
        {
            if (CameraKindIndex == 0)
            {
                CameraState = "已连接（Mock）";
                ServiceHub.Host.Info("Mock 相机合成正常（监控台画面即测试帧）。\r\n切到「RTSP 网络相机」并填好 IP 可测真机连接。", "采集测试帧");
                return;
            }

            if (CameraKindIndex == 2)
            {
                ServiceHub.Host.Info("海康 GigE 相机为 SDK 桩实现（未接入 MVS SDK）。\r\n网络相机请选「RTSP 网络相机」。", "采集测试帧");
                return;
            }

            string ip = (CameraIp ?? "").Trim();
            int port;
            if (!int.TryParse(CameraPort, out port) || port <= 0) port = 554;

            if (string.IsNullOrEmpty(ip))
            {
                ServiceHub.Host.Info("请先填写相机 IP（如 192.168.1.188）。", "采集测试帧");
                return;
            }

            var cfg = new DeviceConfig
            {
                CameraIp = ip,
                CameraPort = port.ToString(),
                RtspChannel = RtspChannel,
                RtspUrlFormat = RtspUrlFormat
            };
            string url = AIris.Core.Camera.RtspCameraSource.BuildUrl(cfg);

            CameraState = "测试中…";

            // 网络探测必须离开 UI 线程（项目铁律：UI 线程禁做网络 IO）
            Task.Run(() =>
            {
                bool ok = TcpProbe(ip, port, 3000);

                Ui(() =>
                {
                    CameraState = ok ? "在线 · RTSP 端口可达" : "离线 · 端口不可达";
                    if (ok)
                    {
                        ServiceHub.Host.Info(
                            string.Format("TCP 端口可达：{0}:{1}\r\nRTSP 地址：{2}\r\n\r\n点「保存」立即应用该相机（不用重启）。", ip, port, url),
                            "采集测试帧");
                    }
                    else
                    {
                        ServiceHub.Host.Info(
                            string.Format("连接失败：{0}:{1} 无响应。\r\n\r\n排查：\r\n① 相机是否上电、网线/交换机是否连通（先 ping {0}）\r\n② 端口是否 554（RTSP 标准端口）\r\n③ 本机网段是否与相机同网段（192.168.1.x）", ip, port, ip),
                            "采集测试帧");
                    }
                });
            });
        }

        /// <summary>TCP 端口探测（带超时；失败/超时一律 false，不抛异常）。</summary>
        private static bool TcpProbe(string ip, int port, int timeoutMs)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var task = client.ConnectAsync(ip, port);
                    if (!task.Wait(timeoutMs)) return false;
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>后台线程回 UI 线程更新绑定属性/弹窗（WPF Dispatcher）。</summary>
        private static void Ui(Action action)
        {
            var app = Application.Current;
            if (app == null) { action(); return; }
            if (app.Dispatcher.CheckAccess()) action();
            else app.Dispatcher.BeginInvoke(action);
        }
    }
}
