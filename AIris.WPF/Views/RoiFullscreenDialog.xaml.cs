using System.Windows;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// ROI 全屏标画弹窗 glue（V0.1.11）：把编辑器的同一 VM 与同一套
    /// <see cref="RoiStageController"/> 装进最大化弹窗，做到"大画面标画 + 数值即时同步"。
    ///
    /// 【为什么模态】工控现场标定是"停下来调参数"的动作，模态窗避免一边跑检测一边改 ROI
    /// 造成判定依据中途变化；帧流仍实时刷新（只读预览），启动/停止取像按钮与主页面同源。
    /// 【退出】标题条 × / 底部"退出全屏" / Esc（基类 CancelCommand 口径）三种都行。
    /// 【生命周期】Closed 里 Dispose 控制器：摘 VM 订阅 + 舞台事件，防止 VM 持有已关闭窗口。
    /// </summary>
    public partial class RoiFullscreenDialog : Kaleidoscope.UI.Views.ChromeDialogWindow
    {
        private readonly RoiStageController _roi;

        public RoiFullscreenDialog(RecipeEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            // 舞台 = FullStage，视口 = StageViewport（弹窗内容区，随窗口缩放）
            _roi = new RoiStageController(FullStage, StageViewport);
            _roi.Bind(vm);

            // Esc 退出：基类 OnPreviewKeyDown 只认 CancelCommand（见 ChromeDialogWindow 注释）
            CancelCommand = new RelayCommand(() => Close());
            Closed += (s, e) => _roi.Dispose();
        }

        private void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
