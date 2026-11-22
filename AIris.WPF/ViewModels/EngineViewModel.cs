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
        public override string Icon => "🧠";   // 大脑 = AI 推理引擎语义（用户定版：⚙ 齿轮与设备页撞图标）

        public EngineViewModel()
        {
            ApplyCommand = new RelayCommand(Apply);
            BackCommand = new RelayCommand(() => ServiceHub.Main.Go("monitor"));
            TestConnectionCommand = new RelayCommand(TestConnection);
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

        // —— 推理协议（V0.1.7 沉库 Kaleidoscope.Infer：OpenAI 兼容 / Anthropic / Ollama / 自研契约） ——

        /// <summary>协议下拉选项（注解各协议适配的部署形态，选对"信封"）。</summary>
        public System.Collections.Generic.List<ChoiceItem> ProtocolChoices { get; } = new System.Collections.Generic.List<ChoiceItem>
        {
            new ChoiceItem("custom",    "自研 FastAPI 契约（/detect · locateanything-batch 部署形态）"),
            new ChoiceItem("openai",    "OpenAI 兼容（/v1/chat/completions · vLLM / llama.cpp / LMDeploy）"),
            new ChoiceItem("ollama",    "Ollama 原生（/api/chat · 本地 Ollama 跑视觉模型）"),
            new ChoiceItem("anthropic", "Anthropic（/v1/messages · 云端 Claude 视觉）"),
        };

        private string _protocol = "custom";
        /// <summary>推理协议 id（INI [Engine] Protocol；决定 InferClient 用哪个 transport 组包）。</summary>
        public string Protocol
        {
            get { return _protocol; }
            set
            {
                if (SetProperty(ref _protocol, value))
                    OnPropertyChanged(nameof(IsCustomProtocol));   // 解码/批处理仅自研契约（服务端参数）可编辑
            }
        }

        /// <summary>解码模式/批处理是否可编辑（属 locateanything-batch 服务端参数；其他协议灰显）。</summary>
        public bool IsCustomProtocol { get { return _protocol == "custom"; } }

        private string _apiKey = "";
        public string ApiKey { get { return _apiKey; } set { SetProperty(ref _apiKey, value); } }

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
        public ICommand TestConnectionCommand { get; }

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
            ApiKey = cfg.ApiKey;
            BatchSize = cfg.BatchSize;
            Protocol = string.IsNullOrEmpty(cfg.Protocol) ? EngineRegistry.DefaultProtocol(cfg.ActiveEngineId) : cfg.Protocol;
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
            if (string.IsNullOrEmpty(cfg.Protocol)) cfg.Protocol = EngineRegistry.DefaultProtocol(card.Card.Id);   // 协议初值
            var engine = EngineRegistry.Create(card.Card.Id, cfg);
            engine.Load(cfg);
            ServiceHub.Monitor.SwapEngine(engine, cfg);
            ServiceHub.Settings.WriteEngine(cfg);
            Protocol = cfg.Protocol;            // 新引擎默认协议回填表单（用户未改协议时保持一致）
            RefreshState();
        }

        /// <summary>测试连接（用表单当前值——未保存——探活；报结果与耗时）。</summary>
        private void TestConnection()
        {
            var ep = new Kaleidoscope.Infer.InferEndpoint
            {
                Protocol = MapProtocol(Protocol),
                BaseUrl = HttpBaseUrl,
                Model = HttpModel,
                ApiKey = ApiKey,
                TimeoutMs = 3000,
            };
            var client = new Kaleidoscope.Infer.InferClient();
            try
            {
                var r = client.ProbeAsync(ep, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                if (r.Ok)
                    ServiceHub.Host.Info("连接成功：" + ep.BaseUrl + "\r\n响应 " + r.LatencyMs + " ms（协议 " + Protocol + "）", "测试连接");
                else
                    ServiceHub.Host.Info("连接失败：" + r.Error + "\r\n检查服务地址/协议是否匹配部署形态。", "测试连接");
            }
            finally { client.Dispose(); }
        }

        /// <summary>协议 id → InferProtocol 枚举（与 HttpDetectionEngine.BuildEndpoint 同口径）。</summary>
        private static Kaleidoscope.Infer.InferProtocol MapProtocol(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "openai": return Kaleidoscope.Infer.InferProtocol.OpenAi;
                case "anthropic": return Kaleidoscope.Infer.InferProtocol.Anthropic;
                case "ollama": return Kaleidoscope.Infer.InferProtocol.Ollama;
                default: return Kaleidoscope.Infer.InferProtocol.CustomJson;
            }
        }

        /// <summary>应用推理参数（落 INI + 实际生效：HTTP 引擎热重载重探活——旧版只落盘不生效）。</summary>
        private void Apply()
        {
            var cfg = ServiceHub.Settings.ReadEngine();
            cfg.DecodeMode = DecodeModeId;
            cfg.BatchSize = BatchSize;
            cfg.HttpBaseUrl = HttpBaseUrl;
            cfg.HttpModel = HttpModel;
            cfg.Protocol = Protocol;
            cfg.ApiKey = ApiKey;
            ServiceHub.Settings.WriteEngine(cfg);

            // 实际生效：当前引擎是 HTTP 型且协议/地址/Key 变了 → Load 重探活刷新状态（UI 即时可见）
            var engine = ServiceHub.Monitor.Engine;
            if (engine is AIris.Core.Detect.HttpDetectionEngine)
            {
                engine.Load(cfg);
                RefreshState();
                ServiceHub.Host.Info("推理参数已应用：协议 " + Protocol + " · "
                    + (engine.IsLoaded ? "服务在线（探活通过）" : "服务未连通（检测回落 Mock 演示）"), "已应用");
            }
            else
            {
                ServiceHub.Host.Info("推理参数已保存。当前引擎（Mock）不使用 HTTP 参数；切换到 HTTP 型引擎时按此配置连接。", "已保存");
            }
        }
    }
}
