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
        public DeviceItem Camera { get; } = new DeviceItem { Icon = "◉", Name = "相机 · RTSP / GigE" };
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

            // 顶栏切换 Mock 数据模式时，本页相机状态点/提示条要即时跟随
            ServiceHub.MockModeChanged += OnMockModeChanged;
        }

        private bool _isMockMode;
        /// <summary>当前是否全局 Mock 数据模式（顶部提示条显隐；相机参数保存后暂不生效）。</summary>
        public bool IsMockMode { get { return _isMockMode; } private set { SetProperty(ref _isMockMode, value); } }

        private void OnMockModeChanged(bool on)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess()) { RefreshStatus(); return; }
            app.Dispatcher.BeginInvoke(new Action(RefreshStatus));
        }

        /// <summary>
        /// 刷新左导航状态点（Load/Save 后调用）。判定规则（Mock 演示态可解释、保存即见变化）：
        ///  · 相机：Mock 数据模式 = 合成画面在线；真实模式读运行中相机 IsConnected（与顶栏胶囊同源）。
        ///  · 光源：亮度参数已给且漫射罩开 = 已配置但仍离机（Mock 无控制器）→ Offline；
        ///    亮度 &lt;= 0 视为未配置。
        ///  · 引导屏/声光：开关开 = 已配置（设备未接 → Offline）；关 = 未配置（橙）。
        ///  · MES：地址空 = 未配置（橙）；有地址且启用 = 在线（绿）；有地址未启用 = 离线（灰）。
        /// </summary>
        private void RefreshStatus()
        {
            // 相机状态判定（与 ServiceHub 装配口径一致）：
            //   Mock 数据模式 = 合成画面始终在线（绿，文案明示"演示中"）；
            //   rtsp = 读运行中相机的真实连接态（保存热切换后立即变绿，断流变灰）；
            //   hik = SDK 桩未接入（橙）；IP 空 = 未配置（橙）。
            IsMockMode = ServiceHub.MockMode;
            var live = ServiceHub.Monitor != null ? ServiceHub.Monitor.Camera : null;

            if (IsMockMode)
            {
                Camera.Status = "Online";
                Camera.StatusText = "演示中 · Mock 合成画面（关闭顶栏「Mock 数据」恢复真机）";
            }
            else if (CameraKindIndex == 0)   // RTSP
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
                    Camera.StatusText = "已配置 · 未连通（点「保存」重连 / 查网线与 IP）";
                }
            }
            else                              // 海康 GigE（SDK 桩）
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
        /// 0 = RTSP 网络相机 / 1 = 海康 GigE 相机。
        /// Mock 合成画面已收口为顶栏「Mock 数据」全局开关，不再占相机类型选项。
        /// 保存时映射回 DeviceConfig.CameraKind（rtsp/hik），ServiceHub 按此装配相机源。
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

        private string _cameraState = "未测试";
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
            // 相机参数保存后立即热切换（不用重启）：RTSP ↔ GigE 随时切回；
            // Mock 数据模式期间保持合成相机（参数落盘，关闭 Mock 后生效）
            ServiceHub.ApplyCamera();
            RefreshStatus();   // 左导航状态点跟随新参数（保存即见变化，不是假状态）
            ServiceHub.Host.Info(ServiceHub.MockMode
                ? "设备参数已保存。当前为 Mock 数据模式，真实相机配置将在关闭 Mock 后生效。"
                : "设备参数已保存。相机配置已立即应用（RTSP / GigE 可随时切换）。", "保存成功");
        }

        /// <summary>相机类型索引 → DeviceConfig.CameraKind 值（rtsp/hik；Mock 已收口为全局开关）。</summary>
        private static string IndexToKind(int index)
        {
            return index == 1 ? "hik" : "rtsp";
        }

        /// <summary>DeviceConfig.CameraKind 值 → ComboBox 索引（只认 rtsp/hik 两档）。</summary>
        private static int KindToIndex(string kind)
        {
            return string.Equals(kind, "hik", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        /// <summary>
        /// 采集测试帧：Mock 数据模式直接确认合成画面；RTSP 后台 TCP 探测相机端口
        /// （不阻塞 UI 线程）；海康 GigE 提示 SDK 桩未接入。
        /// </summary>
        private void TestCamera()
        {
            if (ServiceHub.MockMode)
            {
                CameraState = "已连接（Mock）";
                ServiceHub.Host.Info("当前为 Mock 数据模式，合成画面正常（监控台画面即测试帧）。\r\n"
                    + "关闭顶栏「Mock 数据」后可测试真实相机连接。", "采集测试帧");
                return;
            }

            if (CameraKindIndex == 1)
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
