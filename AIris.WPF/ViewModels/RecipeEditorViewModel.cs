using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AIris.WPF.Services;
using AIris.Core.Model;

namespace AIris.WPF.ViewModels
{
    /// <summary>ROI 编辑行（数值编辑；预览叠加按 0~1000 归一化画框）。</summary>
    public class RoiVm : ViewModelBase
    {
        public ROIRect Model { get; }
        public RoiVm(ROIRect m) { Model = m; }

        public string Id { get { return Model.Id; } set { Model.Id = value; OnPropertyChanged(); } }
        public string Name { get { return Model.Name; } set { Model.Name = value; OnPropertyChanged(); } }
        public int X { get { return Model.X; } set { Model.X = Clamp(value); OnPropertyChanged(); } }
        public int Y { get { return Model.Y; } set { Model.Y = Clamp(value); OnPropertyChanged(); } }
        public int W { get { return Model.W; } set { Model.W = Clamp(value); OnPropertyChanged(); } }
        public int H { get { return Model.H; } set { Model.H = Clamp(value); OnPropertyChanged(); } }
        public string Display { get { return Model.Id + " · " + Model.Name; } }

        private bool _isSelected;
        /// <summary>左侧清单选中高亮（由编辑器 VM 的 SelectedRoi setter 统一维护）。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        private static int Clamp(int v) { return Math.Max(0, Math.Min(1000, v)); }
    }

    /// <summary>通用"值/显示"选项行（判定区域下拉等；Tag=写回值，Content=显示文案）。</summary>
    public class ChoiceItem
    {
        public string Tag { get; private set; }
        public string Content { get; private set; }
        public ChoiceItem(string tag, string content) { Tag = tag; Content = content; }
    }

    /// <summary>工步序列行（编辑器左列；选中态高亮）。</summary>
    public class EditorStepRowVm : ViewModelBase
    {
        public RecipeStep Model { get; }
        public EditorStepRowVm(RecipeStep m, int idx) { Model = m; Index = idx; }

        private int _index;
        public int Index
        {
            get { return _index; }
            set { _index = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndexText)); }
        }
        public string IndexText { get { return _index.ToString(); } }
        public string Name { get { return Model.Name; } }

        private bool _selected;
        public bool Selected
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }
    }

    /// <summary>
    /// 配方编辑器 VM（设计稿 data-page="editor"）：
    /// 左列工步序列（选中编辑），右侧工步配置（自然语言预期零件 = 核心字段）+ 判定规则 + ROI 数值编辑。
    /// 保存 = 回写字段 → RecipeStore.Save；预期零件改一句话即换检测目标（零训练——平台核心卖点）。
    /// </summary>
    public class RecipeEditorViewModel : PageViewModelBase
    {
        public override string Id => "editor";
        public override string Title => "配方编辑器";
        public override string Icon => "◈";

        /// <summary>编辑器不占 dock：从配方中心"编辑"进入（对位设计稿 main 页 nav 5 项）。</summary>
        public override bool IsDockVisible { get { return false; } }

        /// <summary>待编辑配方（ServiceHub.Host.GoEditor 注入；null = 新建草稿）。</summary>
        public static Recipe CurrentEditing { get; set; }

        private Recipe _recipe;

        public RecipeEditorViewModel()
        {
            AddStepCommand = new RelayCommand(AddStep);
            DeleteStepCommand = new RelayCommand(DeleteStep, () => SelectedStep != null);
            AddRoiCommand = new RelayCommand(AddRoi);
            DeleteRoiCommand = new RelayCommand(DeleteRoi, () => SelectedRoi != null);
            SaveCommand = new RelayCommand(Save);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("recipe"));
            GoMonitorCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
            SelectStepCommand = new RelayCommand(p => SelectedStep = p as EditorStepRowVm);
            SelectRoiCommand = new RelayCommand(p => SelectedRoi = p as RoiVm);
            TogglePreviewCommand = new RelayCommand(() => PreviewOn = !PreviewOn);
            FullscreenRoiCommand = new RelayCommand(OpenRoiFullscreen);
            // ROI 集合增删 → "判定区域"下拉选项刷新（RoiChoices 每次 get 重建，集合变时广播即可）
            Rois.CollectionChanged += (s, e) => OnPropertyChanged(nameof(RoiChoices));
        }

        // —— 序列与编辑字段 ——
        public ObservableCollection<EditorStepRowVm> Steps { get; } = new ObservableCollection<EditorStepRowVm>();
        public ObservableCollection<RoiVm> Rois { get; } = new ObservableCollection<RoiVm>();

        private EditorStepRowVm _selectedStep;
        public EditorStepRowVm SelectedStep
        {
            get { return _selectedStep; }
            set
            {
                if (_selectedStep != null) _selectedStep.Selected = false;
                if (SetProperty(ref _selectedStep, value) && _selectedStep != null)
                {
                    _selectedStep.Selected = true;
                    LoadStepFields();
                }
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        private RoiVm _selectedRoi;
        public RoiVm SelectedRoi
        {
            get { return _selectedRoi; }
            set
            {
                if (_selectedRoi != null) _selectedRoi.IsSelected = false;   // 旧选中行去高亮
                if (SetProperty(ref _selectedRoi, value) && _selectedRoi != null)
                    _selectedRoi.IsSelected = true;                          // 新选中行高亮（左侧清单）
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        /// <summary>"判定区域"下拉选项（全画面 + 实际 ROI 清单同源——旧硬编码 ROI-A~D 与集合脱节）。</summary>
        public System.Collections.Generic.List<ChoiceItem> RoiChoices
        {
            get
            {
                var list = new System.Collections.Generic.List<ChoiceItem> { new ChoiceItem("", "全画面（整检步固定）") };
                foreach (var r in Rois) list.Add(new ChoiceItem(r.Id, r.Id));
                return list;
            }
        }

        private string _recipeName = "";
        public string RecipeName { get { return _recipeName; } set { SetProperty(ref _recipeName, value); } }

        private string _recipeDesc = "";
        public string RecipeDesc { get { return _recipeDesc; } set { SetProperty(ref _recipeDesc, value); } }

        private string _partsText = "";
        /// <summary>零件清单文本（顿号分隔，保存时切回 List）。</summary>
        public string PartsText { get { return _partsText; } set { SetProperty(ref _partsText, value); } }

        // 选中步的编辑字段（直接代理到 Model；每次切步 LoadStepFields 载入）
        private string _editStepName = "";
        public string EditStepName { get { return _editStepName; } set { _editStepName = value; WriteStep(s => s.Name = value); } }

        private string _editExpectedPart = "";
        public string EditExpectedPart { get { return _editExpectedPart; } set { _editExpectedPart = value; WriteStep(s => s.ExpectedPart = value); } }

        private int _editExpectation;
        /// <summary>预期变化下拉索引（0 出现 / 1 消失 / 2 整检）。</summary>
        public int EditExpectation
        {
            get { return _editExpectation; }
            set
            {
                _editExpectation = value;
                OnPropertyChanged(nameof(ExpectationText));
                OnPropertyChanged(nameof(IsFinalCheck));
                OnPropertyChanged(nameof(CountEnabled));
                WriteStep(s => s.Expectation = (StepExpectation)value);
                if ((StepExpectation)value == StepExpectation.FinalCheck)
                {
                    // 整检步用全画面，ROI 选择禁用
                    EditRoiId = "";
                }
            }
        }
        public string ExpectationText
        {
            get
            {
                switch ((StepExpectation)_editExpectation)
                {
                    case StepExpectation.Disappear: return "消失（移除）";
                    case StepExpectation.FinalCheck: return "整检（齐套）";
                    default: return "出现（装配到位）";
                }
            }
        }
        public bool IsFinalCheck { get { return (StepExpectation)_editExpectation == StepExpectation.FinalCheck; } }
        public bool CountEnabled { get { return !IsFinalCheck; } }

        private string _editRoiId = "";
        public string EditRoiId { get { return _editRoiId; } set { _editRoiId = value; WriteStep(s => s.RoiId = value); } }

        private double _editIoU = 0.35;
        public double EditIoU { get { return _editIoU; } set { _editIoU = value; WriteStep(s => s.Rule.IoUThreshold = value); } }

        private int _editStableFrames = 3;
        public int EditStableFrames { get { return _editStableFrames; } set { _editStableFrames = value; WriteStep(s => s.Rule.StableFrames = Math.Max(1, value)); } }

        private int? _editExpectedCount;
        public string EditExpectedCountText
        {
            get { return _editExpectedCount.HasValue ? _editExpectedCount.Value.ToString() : ""; }
            set
            {
                int n;
                _editExpectedCount = int.TryParse(value, out n) ? n : (int?)null;
                WriteStep(s => s.Rule.ExpectedCount = _editExpectedCount);
            }
        }

        private bool _editDualChannel;
        public bool EditDualChannel { get { return _editDualChannel; } set { _editDualChannel = value; WriteStep(s => s.Rule.DualChannel = value); } }

        public bool HasSelection { get { return _selectedStep != null; } }

        public ICommand AddStepCommand { get; }
        public ICommand DeleteStepCommand { get; }
        public ICommand AddRoiCommand { get; }
        public ICommand DeleteRoiCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand GoMonitorCommand { get; }
        public ICommand SelectStepCommand { get; }
        public ICommand SelectRoiCommand { get; }
        public ICommand TogglePreviewCommand { get; }
        public ICommand FullscreenRoiCommand { get; }

        /// <summary>全屏标画（V0.1.11）：开独立弹窗放大 ROI 画布（走 Host 门面，VM 不 new Window）。
        /// 弹窗与页内画布共用同一 VM，拖框/数值修改实时双向同步；Esc 或标题条 × 退出。</summary>
        public void OpenRoiFullscreen()
        {
            if (ServiceHub.Host != null) ServiceHub.Host.ShowRoiFullscreen(this);
        }

        // —— ROI 标画实时预览（V0.1.7：编辑页直接开相机出画面，ROI 在实际取像上拖拽标定） ——

        private bool _previewOn;
        /// <summary>预览取像开关（开 = 订阅 RunMonitor.FrameReady + StartPreview 只开相机不判定；
        /// 关 = StopPreview 顺手停相机——检测未运行时不留空转线程）。</summary>
        public bool PreviewOn
        {
            get { return _previewOn; }
            set
            {
                if (SetProperty(ref _previewOn, value))
                {
                    if (value)
                    {
                        if (!_frameHooked)
                        {
                            _frameHooked = true;
                            ServiceHub.Monitor.FrameReady += OnPreviewFrame;
                        }
                        ServiceHub.Monitor.StartPreview();
                    }
                    else
                    {
                        ServiceHub.Monitor.StopPreview();
                        FrameImage = null;   // 回空态（IdleText + 扫描动效恢复）
                    }
                    OnPropertyChanged(nameof(PreviewToggleText));
                }
            }
        }

        /// <summary>开关按钮文案（设计稿"拖拽标画 ROI"口径的入口）。</summary>
        public string PreviewToggleText { get { return _previewOn ? "停止取像" : "启动取像"; } }

        private bool _frameHooked;
        /// <summary>预览帧（BitmapSource，Freeze 后跨线程安全；KxImageStage.FrameSource 绑定源）。</summary>
        private System.Windows.Media.Imaging.BitmapSource _frameImage;
        public System.Windows.Media.Imaging.BitmapSource FrameImage
        {
            get { return _frameImage; }
            private set { SetProperty(ref _frameImage, value); }
        }

        /// <summary>预览帧到达（相机线程 → Dispatcher 收口转 BitmapSource；口径同 MonitorViewModel.OnFrame）。
        /// 2026-11-23 整改：空场不再清帧——Mock 的"空场"是透明帧（台面网格/扫描在舞台层），
        /// 叠出来就是"空台面"；旧版清帧回"等待画面…"会让人误判相机断流。
        /// IdleText 只保留给真正无帧（相机未启动/断流）的状态。</summary>
        private void OnPreviewFrame(object s, AIris.Core.Engine.FrameEventArgs e)
        {
            var img = Imaging.ToBitmapSource(e.Image);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                FrameImage = img ?? FrameImage;
            }));
        }

        public override void OnEnter()
        {
            _recipe = CurrentEditing;
            if (_recipe == null)
            {
                // 直接切页（未经向导）：草稿
                _recipe = new Recipe { Name = "新配方", Description = "", Parts = new System.Collections.Generic.List<string> { "示例零件" } };
                _recipe.Steps.Add(new RecipeStep { Name = "工步 1", ExpectedPart = "示例零件", Expectation = StepExpectation.Appear, Rule = new StepRule() });
            }
            RecipeName = _recipe.Name;
            RecipeDesc = _recipe.Description;
            PartsText = string.Join("、", _recipe.Parts ?? new System.Collections.Generic.List<string>());

            Rois.Clear();
            foreach (var r in _recipe.Rois) Rois.Add(new RoiVm(r));
            if (Rois.Count == 0)
            {
                // 草稿默认给演示布局的 ROI-A/B（新手可直接跑通）
                _recipe.Rois.Add(new ROIRect { Id = "ROI-A", Name = "台面区", X = 80, Y = 130, W = 400, H = 380 });
                _recipe.Rois.Add(new ROIRect { Id = "ROI-B", Name = "螺丝孔位区", X = 660, Y = 270, W = 240, H = 220 });
                foreach (var r in _recipe.Rois) Rois.Add(new RoiVm(r));
            }

            ReloadSteps();

            // 进入编辑器即自动出画面（2026-11-23 整改）：设计稿编辑器画布直接显示实际取像，
            // 旧版要用户先点「启动取像」才出帧，首屏一片网格 + 中央提示文字，标画入口像坏了。
            // StartPreview 内部已判 _running：监控检测进行中则复用 DetectLoop 同源帧，不双开相机。
            PreviewOn = true;
        }

        public override void OnLeave()
        {
            // 离开编辑器即停预览取像：不留空转相机线程与订阅（回到编辑器 OnEnter 会重新自动开）。
            // StopPreview 内部已判 _running——监控检测在跑时不会停相机，只退出预览态。
            PreviewOn = false;
        }

        private void ReloadSteps()
        {
            int keep = _selectedStep != null ? _selectedStep.Index : 1;
            Steps.Clear();
            for (int i = 0; i < _recipe.Steps.Count; i++) Steps.Add(new EditorStepRowVm(_recipe.Steps[i], i + 1));
            var sel = Steps.FirstOrDefault(s => s.Index == keep) ?? Steps.FirstOrDefault();
            SelectedStep = sel;
        }

        /// <summary>把选中步字段载入编辑属性。</summary>
        private void LoadStepFields()
        {
            var s = _selectedStep == null ? null : _selectedStep.Model;
            if (s == null) return;
            _editStepName = s.Name;
            _editExpectedPart = s.ExpectedPart;
            _editExpectation = (int)s.Expectation;
            _editRoiId = s.RoiId;
            _editIoU = s.Rule.IoUThreshold;
            _editStableFrames = s.Rule.StableFrames;
            _editExpectedCount = s.Rule.ExpectedCount;
            _editDualChannel = s.Rule.DualChannel;
            OnPropertyChanged(nameof(EditStepName));
            OnPropertyChanged(nameof(EditExpectedPart));
            OnPropertyChanged(nameof(EditExpectation));
            OnPropertyChanged(nameof(ExpectationText));
            OnPropertyChanged(nameof(IsFinalCheck));
            OnPropertyChanged(nameof(CountEnabled));
            OnPropertyChanged(nameof(EditRoiId));
            OnPropertyChanged(nameof(EditIoU));
            OnPropertyChanged(nameof(EditStableFrames));
            OnPropertyChanged(nameof(EditExpectedCountText));
            OnPropertyChanged(nameof(EditDualChannel));

            // 默认选中：优先本步判定区域，否则第一个 ROI —— 右侧「选中 ROI」数值栏首屏即有值，
            // 画布上选中框显示四角手柄（设计稿默认选中 ROI-A 的口径，2026-11-23 目检发现旧版首屏
            // 一直是"未选择"空态，用户得先点一下框才看得到数值）
            RoiVm prefer = null;
            if (!string.IsNullOrEmpty(_editRoiId))
                prefer = Rois.FirstOrDefault(r => string.Equals(r.Id, _editRoiId, StringComparison.OrdinalIgnoreCase));
            SelectedRoi = prefer ?? Rois.FirstOrDefault();
        }

        /// <summary>编辑属性回写选中步（空选忽略）。</summary>
        private void WriteStep(Action<RecipeStep> apply)
        {
            if (_selectedStep == null) return;
            apply(_selectedStep.Model);
        }

        private void AddStep()
        {
            var step = new RecipeStep
            {
                Name = "工步 " + (_recipe.Steps.Count + 1),
                ExpectedPart = "",
                Expectation = StepExpectation.Appear,
                RoiId = Rois.Count > 0 ? Rois[0].Id : "",
                Rule = new StepRule()
            };
            _recipe.Steps.Add(step);
            ReloadSteps();
            SelectedStep = Steps.LastOrDefault();
        }

        private void DeleteStep()
        {
            if (_selectedStep == null || _recipe.Steps.Count <= 1) return;
            var idx = _selectedStep.Index - 1;
            _recipe.Steps.RemoveAt(idx);
            ReloadSteps();
        }

        private void AddRoi()
        {
            // 新 ROI 默认放画面中部空白（0~1000 归一化），编辑器里改数值
            int n = Rois.Count + 1;
            var roi = new ROIRect { Id = "ROI-" + (char)('A' + Rois.Count % 26), Name = "区域 " + n, X = 360, Y = 420, W = 260, H = 220 };
            _recipe.Rois.Add(roi);
            Rois.Add(new RoiVm(roi));
            SelectedRoi = Rois.LastOrDefault();
        }

        /// <summary>画布上拖拽画新框后落点（V0.1.7：AddRoi 带坐标重载；自动命名+选中新建框）。</summary>
        public void AddRoiAt(int x, int y, int w, int h)
        {
            var roi = new ROIRect { Id = "ROI-" + (char)('A' + Rois.Count % 26), Name = "区域 " + (Rois.Count + 1), X = x, Y = y, W = w, H = h };
            _recipe.Rois.Add(roi);
            Rois.Add(new RoiVm(roi));
            SelectedRoi = Rois.LastOrDefault();
        }

        private void DeleteRoi()
        {
            if (_selectedRoi == null) return;
            // 解除工步引用（引用了该 ROI 的工步回退全画面）
            foreach (var s in _recipe.Steps)
                if (string.Equals(s.RoiId, _selectedRoi.Id, StringComparison.OrdinalIgnoreCase)) s.RoiId = "";
            _recipe.Rois.Remove(_selectedRoi.Model);
            Rois.Remove(_selectedRoi);
            SelectedRoi = null;
            LoadStepFields();
        }

        private void Save()
        {
            _recipe.Name = string.IsNullOrWhiteSpace(RecipeName) ? "未命名配方" : RecipeName.Trim();
            _recipe.Description = RecipeDesc;
            _recipe.Parts = PartsText.Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            // 序号重排（Name 里的"工步 N"跟随）
            for (int i = 0; i < _recipe.Steps.Count; i++)
            {
                var step = _recipe.Steps[i];
                if (step.Name.StartsWith("工步 "))
                {
                    var tail = step.Name.Substring(4);
                    int sp = tail.IndexOf(' ');
                    step.Name = "工步 " + (i + 1) + (sp > 0 ? tail.Substring(sp) : "");
                }
                if (step.Expectation == StepExpectation.FinalCheck) step.Name = "整检 · 零件齐套";
            }
            ServiceHub.Recipes.Save(_recipe);
            CurrentEditing = _recipe;
            ServiceHub.Host.Info("配方已保存：「" + _recipe.Name + "」。到配方中心启用后即生效。", "保存成功");
            ReloadSteps();
        }
    }
}
