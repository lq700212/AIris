using System;
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
        public DeviceItem Camera { get; } = new DeviceItem { Icon = "◉", Name = "GigE 相机 · 海康" };
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
            Camera.Status = "Online";
            Camera.StatusText = "在线 · Mock 合成画面";

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
            CameraIp = d.CameraIp;
            CameraPort = d.CameraPort;
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
                CameraIp = CameraIp,
                CameraPort = CameraPort,
                CameraExposureUs = ExposureUs,
                LightBrightness = LightBrightness,
                LightDiffuserOn = DiffuserOn,
                GuideScreenOn = GuideScreenOn,
                BeaconOn = BeaconOn,
                MesUrl = MesUrl,
                MesEnabled = MesEnabled
            };
            ServiceHub.Settings.WriteDevices(d);
            RefreshStatus();   // 左导航状态点跟随新参数（保存即见变化，不是假状态）
            ServiceHub.Host.Info("设备参数已保存（Mock 演示态即存即生效；真机接入后按此参数连接）。", "保存成功");
        }

        /// <summary>采集测试帧（Mock 态直接确认；真机接入后触发抓帧验证）。</summary>
        private void TestCamera()
        {
            ServiceHub.Host.Info("Mock 相机合成正常（监控台画面即测试帧）。\r\n真机接入后此处触发一次抓帧并回显。", "采集测试帧");
        }
    }
}
