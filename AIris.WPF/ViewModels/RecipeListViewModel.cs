using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AIris.WPF.Services;
using AIris.Core.Model;

namespace AIris.WPF.ViewModels
{
    /// <summary>配方卡行 VM（包 Recipe 展示字段 + 卡上操作回调）。</summary>
    public class RecipeCardVm : ViewModelBase
    {
        public Recipe Model { get; }
        public RecipeCardVm(Recipe model, RecipeListViewModel owner)
        {
            Model = model;
            EditCommand = new RelayCommand(() => ServiceHub.Host.GoEditor(model));
            ToggleCommand = new RelayCommand(() => owner.Toggle(this));
            DeleteCommand = new RelayCommand(() => owner.Delete(this));
        }

        public string Name { get { return Model.Name; } }
        public string Description { get { return Model.Description; } }
        public string PartsLine { get { return "检测类目：" + (Model.Parts == null || Model.Parts.Count == 0 ? "—" : string.Join("、", Model.Parts)); } }
        public string MetaLine
        {
            get
            {
                return "工步 " + Model.StepCount + " 步 · ROI " + Model.RoiCount + " 个 · 编辑于 " + Model.UpdatedAt.ToString("MM-dd");
            }
        }
        public bool Enabled { get { return Model.Enabled; } }
        public string EnabledText { get { return Model.Enabled ? "启用中" : "已停用"; } }
        public string ToggleText { get { return Model.Enabled ? "停用" : "启用"; } }

        public ICommand EditCommand { get; }
        public ICommand ToggleCommand { get; }
        public ICommand DeleteCommand { get; }

        /// <summary>外部改状态后刷新卡显示。</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(EnabledText));
            OnPropertyChanged(nameof(ToggleText));
            OnPropertyChanged(nameof(MetaLine));
        }
    }

    /// <summary>
    /// 配方中心 VM（设计稿 data-page="recipe"）：列表 CRUD/启停用。
    /// 启用语义：全局唯一启用（RecipeStore.Enable 互斥），监控台判定以启用配方为准。
    /// </summary>
    public class RecipeListViewModel : PageViewModelBase
    {
        public override string Id => "recipe";
        public override string Title => "配方中心";
        public override string Icon => "▤";

        public RecipeListViewModel()
        {
            NewRecipeCommand = new RelayCommand(() => { if (ServiceHub.Host.ShowNewRecipe()) Load(); });
            GoMonitorCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
        }

        public ObservableCollection<RecipeCardVm> Cards { get; } = new ObservableCollection<RecipeCardVm>();

        private bool _isEmpty;
        /// <summary>空态显隐（没有配方）。</summary>
        public bool IsEmpty { get { return _isEmpty; } private set { SetProperty(ref _isEmpty, value); } }

        private string _enabledNote = "启用 0 / 共 0";
        public string EnabledNote { get { return _enabledNote; } private set { SetProperty(ref _enabledNote, value); } }

        public ICommand NewRecipeCommand { get; }
        public ICommand GoMonitorCommand { get; }

        public override void OnEnter() { Load(); }

        /// <summary>重载列表（进入页面/新建/删除/启停后调用）。</summary>
        public void Load()
        {
            var all = ServiceHub.Recipes.ListAll();
            Cards.Clear();
            foreach (var r in all) Cards.Add(new RecipeCardVm(r, this));
            IsEmpty = Cards.Count == 0;
            EnabledNote = "启用 " + all.Count(r => r.Enabled) + " / 共 " + all.Count;
        }

        /// <summary>启用/停用（互斥切换）。</summary>
        public void Toggle(RecipeCardVm card)
        {
            if (card.Model.Enabled) ServiceHub.Recipes.Disable(card.Model.Name);
            else ServiceHub.Recipes.Enable(card.Model.Name);
            // 启用切换同步到监控台（运行编排以启用配方为准）
            ServiceHub.Monitor.LoadRecipe(ServiceHub.Recipes.GetEnabled());
            Load();
        }

        /// <summary>删除（二次确认在 Host.Confirm 内）。</summary>
        public void Delete(RecipeCardVm card)
        {
            if (!ServiceHub.Host.Confirm("删除配方",
                "确定删除配方「" + card.Model.Name + "」？\r\n工步、ROI 与判定规则一并删除；历史审计存档保留。",
                "删除", true)) return;
            ServiceHub.Recipes.Delete(card.Model.Name);
            if (card.Model.Enabled) ServiceHub.Monitor.LoadRecipe(null);
            Load();
        }
    }
}
