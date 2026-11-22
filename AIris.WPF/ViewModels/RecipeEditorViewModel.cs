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

        private static int Clamp(int v) { return Math.Max(0, Math.Min(1000, v)); }
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
            set { SetProperty(ref _selected, value); OnPropertyChanged(nameof(BgKey)); }
        }
        public string BgKey { get { return _selected ? "ThemePrimaryDim" : "Transparent"; } }
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
            set { SetProperty(ref _selectedRoi, value); OnPropertyChanged(nameof(HasSelection)); }
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
