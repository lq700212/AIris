using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AIris.Core.Store;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.ViewModels
{
    /// <summary>审计表格行 VM（包 AuditRecord 展示 + NG 左红条）。</summary>
    public class AuditRowVm : ViewModelBase
    {
        public AuditRecord Model { get; }
        public AuditRowVm(AuditRecord m) { Model = m; }

        public string Time { get { return Model.Time.ToString("HH:mm:ss"); } }
        public string Sn { get { return Model.Sn; } }
        public string Step { get { return Model.StepIndex + " · " + Model.StepName; } }
        public string RoiId { get { return Model.RoiId; } }
        public string Verdict { get { return Model.Verdict; } }
        public string VerdictDetail { get { return Model.Verdict + (string.IsNullOrEmpty(Model.NgReason) ? "" : " · " + Model.NgReason); } }
        public bool IsNg { get { return string.Equals(Model.Verdict, "NG", StringComparison.OrdinalIgnoreCase); } }
        public string NgColor { get { return IsNg ? "ThemeNg" : "ThemeOk"; } }
    }

    /// <summary>
    /// 审计追溯 VM（设计稿 data-page="audit"）：筛选 + KPI + 记录表 + 存档详情弹窗。
    /// 数据源 JsonAuditStore.Query（JSONL 按天；后续换 SQLite 只动 Store）。
    /// </summary>
    public class AuditViewModel : PageViewModelBase
    {
        public override string Id => "audit";
        public override string Title => "审计追溯";
        public override string Icon => "▦";

        public AuditViewModel()
        {
            QueryCommand = new RelayCommand(Load);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
        }

        public ObservableCollection<AuditRowVm> Rows { get; } = new ObservableCollection<AuditRowVm>();

        private string _dateText = DateTime.Now.ToString("yyyy-MM-dd");
        /// <summary>日期筛选（yyyy-MM-dd；空 = 全部）。</summary>
        public string DateText { get { return _dateText; } set { SetProperty(ref _dateText, value); } }

        private int _verdictIndex;
        /// <summary>判定筛选下拉（0 全部 / 1 OK / 2 NG）。</summary>
        public int VerdictIndex { get { return _verdictIndex; } set { SetProperty(ref _verdictIndex, value); } }

        private string _verdictFilter = "全部";
        public string VerdictFilter { get { return _verdictFilter; } set { SetProperty(ref _verdictFilter, value); } }

        private int _okCount, _ngCount, _totalCount;
        public int OkCount { get { return _okCount; } private set { SetProperty(ref _okCount, value); } }
        public int NgCount { get { return _ngCount; } private set { SetProperty(ref _ngCount, value); } }
        public int TotalCount { get { return _totalCount; } private set { SetProperty(ref _totalCount, value); } }

        public ICommand QueryCommand { get; }
        public ICommand BackCommand { get; }

        public override void OnEnter() { Load(); }

        /// <summary>按筛选查询（单次最多 500 行，超出提示分页语义）。</summary>
        public void Load()
        {
            string dateYmd = null;
            DateTime d;
            if (DateTime.TryParse(DateText, out d)) dateYmd = d.ToString("yyyyMMdd");
            string verdict = _verdictIndex == 1 ? "OK" : _verdictIndex == 2 ? "NG" : "全部";

            var list = new JsonAuditStore().Query(dateYmd, verdict, null, 500);
            Rows.Clear();
            foreach (var r in list) Rows.Add(new AuditRowVm(r));
            TotalCount = list.Count;
            OkCount = list.Count(r => string.Equals(r.Verdict, "OK", StringComparison.OrdinalIgnoreCase));
            NgCount = list.Count(r => string.Equals(r.Verdict, "NG", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>打开存档详情弹窗（表格行"⋯"）。</summary>
        public void OpenShot(AuditRowVm row)
        {
            if (row != null) ServiceHub.Host.ShowShot(row.Model);
        }

        /// <summary>表格行"⋯"入口（View 直调）。</summary>
        public void OpenShotByModel(AuditRecord rec) { if (rec != null) ServiceHub.Host.ShowShot(rec); }
    }
}
