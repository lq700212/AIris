using System.Windows;
using System.Windows.Controls;
using AIris.WPF.ViewModels;

namespace AIris.WPF.Views
{
    /// <summary>
    /// 配方编辑器 glue（V0.1.11 瘦身）：只负责把 DataContext 交给 ROI 交互控制器，
    /// 拖框/手柄/空白画框/数值联动全部收进 <see cref="RoiStageController"/>——
    /// 页面内画布与全屏弹窗（RoiFullscreenDialog）共用同一份交互，杜绝两套实现漂移。
    ///
    /// 【历史沿革】V0.1.7 将 ROI 标画从"纯数值"升级为"实际取像上拖拽标画"，
    /// 交互原写在本文件；V0.1.11 因全屏标画需求抽出控制器，行为逐条保持一致。
    /// </summary>
    public partial class RecipeEditorPage : UserControl
    {
        private readonly RoiStageController _roi;

        public RecipeEditorPage()
        {
            InitializeComponent();
            // 舞台 = RoiStage，视口 = 外层 RoiViewport（画布按画面比例内接其中并居中）
            _roi = new RoiStageController(RoiStage, RoiViewport);
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // VM 未就绪/切页时 Bind 会解旧绑新（控制器内部判空）
            _roi.Bind(DataContext as RecipeEditorViewModel);
        }
    }
}
