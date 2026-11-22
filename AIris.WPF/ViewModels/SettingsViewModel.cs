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

        /// <summary>设备项（左列表行）。</summary>
        public class DeviceItem
        {
            public string Icon { get; set; }
            public string Name { get; set; }
        }

        public SettingsViewModel()
        {
            SaveCommand = new RelayCommand(Save);
            TestCameraCommand = new RelayCommand(TestCamera);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
        }

        /// <summary>设备列表（左列；顺序即设计稿）。</summary>
        public DeviceItem[] Devices { get; } = new[]
        {
            new DeviceItem { Icon = "◉", Name = "GigE 相机 · 海康" },
            new DeviceItem { Icon = "☀", Name = "光源控制器" },
            new DeviceItem { Icon = "▢", Name = "引导屏" },
            new DeviceItem { Icon = "♪", Name = "声光报警灯" },
            new DeviceItem { Icon = "⇄", Name = "MES · HTTP" },
        };

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
            ServiceHub.Host.Info("设备参数已保存（Mock 演示态即存即生效；真机接入后按此参数连接）。", "保存成功");
        }

        /// <summary>采集测试帧（Mock 态直接确认；真机接入后触发抓帧验证）。</summary>
        private void TestCamera()
        {
            ServiceHub.Host.Info("Mock 相机合成正常（监控台画面即测试帧）。\r\n真机接入后此处触发一次抓帧并回显。", "采集测试帧");
        }
    }
}
