using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AIris.Core.Detect;
using AIris.WPF.Services;

namespace AIris.WPF.ViewModels
{
    /// <summary>引擎卡行 VM（设计稿 engine 页三张卡：Mock + LocateAnything + Qwen2.5-VL）。</summary>
    public class EngineCardVm : ViewModelBase
    {
        public EngineRegistry.EngineCard Card { get; }
        private readonly EngineViewModel _owner;

        public EngineCardVm(EngineRegistry.EngineCard card, EngineViewModel owner)
        {
            Card = card;
            _owner = owner;
            SwitchCommand = new RelayCommand(() => owner.SwitchTo(this));
        }

        public string Name { get { return Card.Name; } }
        public string LicenseNote { get { return Card.LicenseNote; } }
        public bool IsMock { get { return Card.IsMock; } }

        private bool _active;
        /// <summary>是否当前引擎（主色描边焦点卡 + "当前引擎"胶囊）。</summary>
        public bool Active
        {
            get { return _active; }
            set { SetProperty(ref _active, value); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(SwitchText)); OnPropertyChanged(nameof(SwitchEnabled)); }
        }

        public string StateText { get { return _active ? "当前引擎" : (Card.IsMock ? "可随时切回" : "可切换"); } }
        public string SwitchText { get { return _active ? "使用中" : "切换到此引擎"; } }
        public bool SwitchEnabled { get { return !_active; } }

        public ICommand SwitchCommand { get; }

        /// <summary>外部刷新态。</summary>
        public void Refresh() { Active = _owner.IsActive(IdOf(Card)); }

        private static string IdOf(EngineRegistry.EngineCard c)
        {
            return c.Id;
        }
    }

    /// <summary>
    /// 检测引擎 VM（设计稿 data-page="engine"）：引擎卡切换（二次确认）+ 推理参数 + 服务状态。
    /// 大模型未接入：HTTP 引擎 Load 后 IsLoaded=false，检测走 RunMonitor 降级回落 Mock（UI 明示）。
    /// </summary>
    public class EngineViewModel : PageViewModelBase
    {
        public override string Id => "engine";
        public override string Title => "检测引擎";
        public override string Icon => "⚙";

        public EngineViewModel()
        {
            ApplyCommand = new RelayCommand(Apply);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
        }

        public ObservableCollection<EngineCardVm> Cards { get; } = new ObservableCollection<EngineCardVm>();

        // —— 推理参数（EngineConfig 落 INI） ——
        private string _decodeMode = "hybrid（MTP 优先）";
        public string DecodeMode
        {
            get { return _decodeMode; }
            set { SetProperty(ref _decodeMode, value); OnPropertyChanged(nameof(DecodeModeId)); }
        }
        /// <summary>下拉框选中的纯模式 id（写入 EngineConfig）。</summary>
        public string DecodeModeId
        {
            get
            {
                if (_decodeMode.StartsWith("fast")) return "fast";
                if (_decodeMode.StartsWith("slow")) return "slow";
                return "hybrid";
            }
        }

        private int _batchSize = 8;
        public int BatchSize { get { return _batchSize; } set { SetProperty(ref _batchSize, value); } }

        private string _httpBaseUrl = "";
        public string HttpBaseUrl { get { return _httpBaseUrl; } set { SetProperty(ref _httpBaseUrl, value); } }

        private string _httpModel = "";
        public string HttpModel { get { return _httpModel; } set { SetProperty(ref _httpModel, value); } }

        // —— 服务状态（只读） ——
        private string _memoryNote = "—";
        public string MemoryNote { get { return _memoryNote; } private set { SetProperty(ref _memoryNote, value); } }

        private long _lastMs;
        public long LastMs { get { return _lastMs; } private set { SetProperty(ref _lastMs, value); } }

        private long _totalCalls;
        public long TotalCalls { get { return _totalCalls; } private set { SetProperty(ref _totalCalls, value); } }

        private string _engineState = "—";
        /// <summary>引擎态一句话（"Mock 演示 · 已就绪"/"HTTP 桩 · 未接入，检测回落 Mock"）。</summary>
        public string EngineState { get { return _engineState; } private set { SetProperty(ref _engineState, value); } }

        private string _activeEngineName = "";
        public string ActiveEngineName { get { return _activeEngineName; } private set { SetProperty(ref _activeEngineName, value); } }

        public ICommand ApplyCommand { get; }
        public ICommand BackCommand { get; }

        public override void OnEnter() { Load(); }

        /// <summary>装载卡片 + 参数 + 状态。</summary>
        public void Load()
        {
            if (Cards.Count == 0)
            {
                foreach (var c in EngineRegistry.Cards) Cards.Add(new EngineCardVm(c, this));
            }
            var cfg = ServiceHub.Settings.ReadEngine();
            HttpBaseUrl = cfg.HttpBaseUrl;
            HttpModel = cfg.HttpModel;
            BatchSize = cfg.BatchSize;
            _decodeMode = cfg.DecodeMode == "fast" ? "fast（MTP）" : cfg.DecodeMode == "slow" ? "slow（AR）" : "hybrid（MTP 优先）";
            OnPropertyChanged(nameof(DecodeMode));

            RefreshState();
        }

        /// <summary>刷新引擎卡高亮与状态行。</summary>
        public void RefreshState()
        {
            var activeId = ServiceHub.Monitor.Engine == null ? "mock" : ServiceHub.Monitor.Engine.Id;
            ActiveEngineName = ServiceHub.Monitor.Engine == null ? "Mock 演示引擎" : ServiceHub.Monitor.Engine.Name;
            foreach (var c in Cards) c.Refresh();
            if (ServiceHub.Monitor.Engine != null)
            {
                EngineState = ServiceHub.Monitor.Engine.IsLoaded
                    ? ServiceHub.Monitor.Engine.Name + " · 已就绪"
                    : ServiceHub.Monitor.Engine.Name + " · 未接入，检测回落 Mock 演示";
                LastMs = ServiceHub.Monitor.Engine.Stats.LastElapsedMs;
                TotalCalls = ServiceHub.Monitor.Engine.Stats.TotalCalls;
                MemoryNote = ServiceHub.Monitor.Engine.Stats.MemoryNote;
            }
        }

        public bool IsActive(string engineId)
        {
            var cur = ServiceHub.Monitor.Engine;
            if (engineId == "mock")
                return cur == null || cur.Id == "mock" || !cur.IsLoaded;
            return cur != null && cur.Id == engineId;
        }

        /// <summary>切换引擎（Host 二次确认 → SwapEngine → 落 INI → 刷新）。</summary>
        public void SwitchTo(EngineCardVm card)
        {
            if (!ServiceHub.Host.ShowEngineSwitch(card.Card)) return;
            var cfg = ServiceHub.Settings.ReadEngine();
            cfg.ActiveEngineId = card.Card.Id;
            var engine = EngineRegistry.Create(card.Card.Id);
            engine.Load(cfg);
            ServiceHub.Monitor.SwapEngine(engine, cfg);
            ServiceHub.Settings.WriteEngine(cfg);
            RefreshState();
        }

        /// <summary>应用推理参数（落 INI；引擎参数需重载时提示）。</summary>
        private void Apply()
        {
            var cfg = ServiceHub.Settings.ReadEngine();
            cfg.DecodeMode = DecodeModeId;
            cfg.BatchSize = BatchSize;
            cfg.HttpBaseUrl = HttpBaseUrl;
            cfg.HttpModel = HttpModel;
            ServiceHub.Settings.WriteEngine(cfg);
            ServiceHub.Host.Info("推理参数已保存。引擎侧参数在下次引擎加载时生效（HTTP 引擎接入后按此配置连接）。", "已保存");
        }
    }
}
