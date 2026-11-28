using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AIris.Core.Detect;
using AIris.Core.Model;
using AIris.WPF.Services;

namespace AIris.WPF.ViewModels
{
    /// <summary>引擎卡行 VM（设计稿 engine 页内置卡 + 自定义卡 + 页头「＋ 添加引擎」入口）。</summary>
    public class EngineCardVm : ViewModelBase
    {
        public EngineCardDefinition Card { get; }
        private readonly EngineViewModel _owner;

        public EngineCardVm(EngineCardDefinition card, EngineViewModel owner)
        {
            Card = card;
            _owner = owner;
            SwitchCommand = new RelayCommand(() => owner.SwitchTo(this));
            DeleteCommand = new RelayCommand(() => owner.DeleteEngine(this));
        }

        public string Name { get { return Card.Name; } }
        public string LicenseNote { get { return Card.LicenseNote; } }
        public bool IsMock { get { return Card.IsMock; } }

        /// <summary>是否可删除：仅「＋ 添加引擎」创建的自定义卡可删（内置卡无删除入口）。</summary>
        public bool CanDelete { get { return !Card.IsBuiltin && !Card.IsMock; } }

        /// <summary>服务参数小字（卡片底部）：协议 · 模型名——切卡前一眼看清连的是哪套服务。</summary>
        public string ServiceLine
        {
            get
            {
                if (Card.IsMock) return "服务参数：内置 · 无 HTTP 依赖";
                string model = string.IsNullOrEmpty(Card.HttpModel) ? "（模型名待填）" : Card.HttpModel;
                return "服务参数：" + (string.IsNullOrEmpty(Card.Protocol) ? "custom" : Card.Protocol) + " · " + model;
            }
        }

        /// <summary>许可证胶囊语义档：Mock=Warn（演示）/ LocateAnything=Ng（仅研究不可商用）/ 其余=Ok（Apache 商用）。</summary>
        public string LicenseTag
        {
            get
            {
                if (Card.IsMock) return "Warn";
                return string.Equals(Card.Id, "locateanything-http", StringComparison.OrdinalIgnoreCase) ? "Ng" : "Ok";
            }
        }

        private bool _active;
        /// <summary>是否当前引擎（主色描边焦点卡 + "当前引擎"胶囊）。Mock 数据模式下全部为 false。</summary>
        public bool Active
        {
            get { return _active; }
            set { SetProperty(ref _active, value); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(SwitchText)); OnPropertyChanged(nameof(SwitchEnabled)); }
        }

        public string StateText { get { return _active ? "当前引擎" : "可切换"; } }
        public string SwitchText { get { return _active ? "使用中" : "切换到此引擎"; } }
        public bool SwitchEnabled { get { return !_active; } }

        public ICommand SwitchCommand { get; }
        public ICommand DeleteCommand { get; }

        /// <summary>外部刷新态：Active=选中（Mock 数据模式下不高亮任何真实卡）。</summary>
        public void Refresh()
        {
            Active = _owner.IsActive(Card.Id);
        }
    }

    /// <summary>
    /// 检测引擎 VM（设计稿 data-page="engine"）：引擎卡切换（二次确认）+ 添加/删除自定义引擎 + 推理参数 + 服务状态。
    /// 大模型未接入：HTTP 引擎 Load 后 IsLoaded=false，检测走 RunMonitor 降级回落 Mock（UI 明示）。
    /// 每张卡独立保存连接参数（Data\EngineCards.json），切换即回填表单——换模型不重复手填。
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
            AddEngineCommand = new RelayCommand(AddEngine);

            // Mock 数据模式由顶栏全局切换：本页提示条/卡片高亮要即时跟随（不依赖 OnEnter 重进）
            ServiceHub.MockModeChanged += OnMockModeChanged;
        }

        public ObservableCollection<EngineCardVm> Cards { get; } = new ObservableCollection<EngineCardVm>();

        // —— 推理参数（EngineConfig 落 INI） ——

        /// <summary>
        /// 解码模式下拉选项（展示文案，与 EngineConfig.DecodeMode 的 id 一一对应：
        /// hybrid→"hybrid（MTP 优先）"、fast→"fast（MTP）"、slow→"slow（AR）"）。
        /// 【必须给 Items】WPF ComboBox 的 SelectedItem 绑字符串时，Items 也必须是字符串集合，
        /// 否则选中值对不上、框内永远空白（AGENTS.md 红线；本页 V0.1.9 目检抓获）。
        /// </summary>
        public System.Collections.Generic.List<string> DecodeModeChoices { get; } = new System.Collections.Generic.List<string>
        {
            "hybrid（MTP 优先）",
            "fast（MTP）",
            "slow（AR）",
        };

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

        private string _testState = "idle";
        /// <summary>测试连接就地反馈状态：idle / ok / fail（XAML 胶囊颜色与文案由它驱动）。</summary>
        public string TestState { get { return _testState; } private set { SetProperty(ref _testState, value); } }

        private string _testText = "尚未测试";
        /// <summary>测试连接结果文本（在线=耗时；不可达=原因，不用翻日志）。</summary>
        public string TestText { get { return _testText; } private set { SetProperty(ref _testText, value); } }

        private string _activeEngineName = "";
        public string ActiveEngineName { get { return _activeEngineName; } private set { SetProperty(ref _activeEngineName, value); } }

        private bool _isMockMode;
        /// <summary>当前是否全局 Mock 数据模式（页面顶部提示条显隐；真实引擎卡暂不生效）。</summary>
        public bool IsMockMode { get { return _isMockMode; } private set { SetProperty(ref _isMockMode, value); } }

        public ICommand ApplyCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand AddEngineCommand { get; }

        public override void OnEnter() { Load(); }

        /// <summary>装载卡片 + 参数 + 状态（进页面时卡列只建一次；增删卡走 ReloadCards）。</summary>
        public void Load()
        {
            if (Cards.Count == 0) ReloadCards();
            LoadForm(ServiceHub.Settings.ReadEngine());
            RefreshState();
        }

        /// <summary>重建卡列：内置卡（参数已合并现场覆盖）+ 自定义卡（Data\EngineCards.json）。</summary>
        private void ReloadCards()
        {
            Cards.Clear();
            foreach (var c in EngineRegistry.AllCards(ServiceHub.EngineCards))
                Cards.Add(new EngineCardVm(c, this));
        }

        /// <summary>把 EngineConfig 回填到推理参数表单。</summary>
        private void LoadForm(EngineConfig cfg)
        {
            HttpBaseUrl = cfg.HttpBaseUrl;
            HttpModel = cfg.HttpModel;
            ApiKey = cfg.ApiKey;
            BatchSize = cfg.BatchSize;
            Protocol = string.IsNullOrEmpty(cfg.Protocol)
                ? EngineRegistry.DefaultProtocol(cfg.ActiveEngineId, ServiceHub.EngineCards)
                : cfg.Protocol;
            _decodeMode = cfg.DecodeMode == "fast" ? "fast（MTP）" : cfg.DecodeMode == "slow" ? "slow（AR）" : "hybrid（MTP 优先）";
            OnPropertyChanged(nameof(DecodeMode));
        }

        /// <summary>刷新引擎卡高亮与状态行（Mock 数据模式 / 真实引擎两种口径）。</summary>
        public void RefreshState()
        {
            IsMockMode = ServiceHub.MockMode;
            var monitor = ServiceHub.Monitor;

            if (ServiceHub.MockMode)
            {
                // Mock 模式：不把任何真实卡标成"当前引擎"（引擎实例是演示替身，不是卡）
                ActiveEngineName = "Mock 数据模式";
                EngineState = "Mock 数据模式 · 相机与引擎均为演示数据（关闭顶栏「Mock 数据」恢复真实配置）";
                LastMs = monitor == null || monitor.Engine == null ? 0 : monitor.Engine.Stats.LastElapsedMs;
                TotalCalls = monitor == null || monitor.Engine == null ? 0 : monitor.Engine.Stats.TotalCalls;
                MemoryNote = "—";
            }
            else if (monitor != null && monitor.Engine != null)
            {
                ActiveEngineName = monitor.Engine.Name;
                EngineState = monitor.Engine.IsLoaded
                    ? monitor.Engine.Name + " · 已就绪"
                    : monitor.Engine.Name + " · 未接入，检测回落 Mock 演示";
                // 单帧耗时取"实际执行者"（未接入时是 Mock 替身的耗时）——否则回落态永远显示 0 ms，
                // 与画面明明在出框的事实矛盾
                LastMs = string.IsNullOrEmpty(monitor.LastExecEngineName)
                    ? monitor.Engine.Stats.LastElapsedMs
                    : monitor.LastExecElapsedMs;
                TotalCalls = monitor.Engine.Stats.TotalCalls;
                MemoryNote = monitor.Engine.Stats.MemoryNote;
            }
            else
            {
                ActiveEngineName = "—";
                EngineState = "引擎未装配";
            }

            foreach (var c in Cards) c.Refresh();
        }

        /// <summary>是否选中该引擎（严格按当前引擎实例）。
        /// Mock 数据模式下全部返回 false——当前跑的是演示替身，不是任何一张真实卡。</summary>
        public bool IsActive(string engineId)
        {
            if (ServiceHub.MockMode) return false;
            var cur = ServiceHub.Monitor == null ? null : ServiceHub.Monitor.Engine;
            return cur != null && string.Equals(cur.Id, engineId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Mock 模式切换通知（可能来自非 UI 线程）→ 调度回 UI 线程刷新本页。</summary>
        private void OnMockModeChanged(bool on)
        {
            var app = System.Windows.Application.Current;
            if (app == null || app.Dispatcher.CheckAccess()) { RefreshState(); return; }
            app.Dispatcher.BeginInvoke(new Action(RefreshState));
        }

        /// <summary>切换引擎（Host 二次确认 → 卡参数写入配置 → SwapEngine → 落 INI → 表单回填 + 刷新）。
        /// Mock 数据模式下切换真实卡 = 同时退出 Mock（相机切回设备页真实相机）。
        /// 装载（探活）统一由 RunMonitor.SwapEngine 执行，这里不重复 Load（否则 HTTP 引擎白等两轮探活）。</summary>
        public void SwitchTo(EngineCardVm card)
        {
            if (card == null) return;
            if (!ServiceHub.Host.ShowEngineSwitch(card.Card)) return;

            var cfg = ServiceHub.Settings.ReadEngine();
            cfg.ActiveEngineId = card.Card.Id;

            // 该卡的独立连接参数 → 当前生效配置（换模型即回填，不用重填表单）
            cfg.Protocol = string.IsNullOrEmpty(card.Card.Protocol)
                ? EngineRegistry.DefaultProtocol(card.Card.Id, ServiceHub.EngineCards)
                : card.Card.Protocol;
            cfg.HttpBaseUrl = card.Card.HttpBaseUrl ?? "";
            cfg.HttpModel = card.Card.HttpModel ?? "";
            cfg.ApiKey = card.Card.ApiKey ?? "";
            if (card.Card.HttpTimeoutMs > 0) cfg.HttpTimeoutMs = card.Card.HttpTimeoutMs;
            ServiceHub.Settings.WriteEngine(cfg);

            if (ServiceHub.MockMode)
            {
                // 退出 Mock：SetMockMode 内部按刚写入的卡参数建真实引擎，并同步切回真实相机
                ServiceHub.SetMockMode(false);
            }
            else
            {
                var engine = EngineRegistry.Create(card.Card.Id, cfg, ServiceHub.EngineCards);
                ServiceHub.Monitor.SwapEngine(engine, cfg);
            }

            LoadForm(cfg);            // 新引擎的连接参数回填表单（用户未改协议时保持一致）
            RefreshState();
        }

        /// <summary>添加自定义引擎（弹窗 → Data\EngineCards.json → 重建卡列）。</summary>
        private void AddEngine()
        {
            if (!ServiceHub.Host.ShowAddEngine()) return;
            ReloadCards();
            RefreshState();
            ServiceHub.Host.Info("引擎卡已添加。填好连接参数后点卡片上的「切换到此引擎」即可使用。", "已添加");
        }

        /// <summary>删除自定义引擎卡（内置卡无删除入口；删的是当前激活卡时回落 Mock）。</summary>
        public void DeleteEngine(EngineCardVm vm)
        {
            if (vm == null || vm.Card.IsBuiltin) return;
            if (!ServiceHub.Host.Confirm("删除检测引擎",
                "删除自定义引擎卡「" + vm.Card.Name + "」？\r\n记录同步从 Data\\EngineCards.json 移除，模型服务本身不受影响。",
                "删除", true)) return;

            ServiceHub.EngineCards.DeleteCustom(vm.Card.Id);

            var cfg = ServiceHub.Settings.ReadEngine();
            if (string.Equals(cfg.ActiveEngineId, vm.Card.Id, StringComparison.OrdinalIgnoreCase))
            {
                // 当前激活卡被删：落到第一张内置真实卡，避免 ActiveEngineId 悬空。
                // （Mock 已不是卡，不能再用 "mock" 当回落目标。）
                var fallback = EngineRegistry.Builtin[0];
                cfg.ActiveEngineId = fallback.Id;
                cfg.Protocol = fallback.Protocol;
                cfg.HttpBaseUrl = fallback.HttpBaseUrl;
                cfg.HttpModel = fallback.HttpModel;
                cfg.ApiKey = fallback.ApiKey;
                cfg.HttpTimeoutMs = fallback.HttpTimeoutMs;
                ServiceHub.Settings.WriteEngine(cfg);
                if (!ServiceHub.MockMode)
                    ServiceHub.Monitor.SwapEngine(EngineRegistry.Create(fallback.Id, cfg, ServiceHub.EngineCards), cfg);
            }
            ReloadCards();
            RefreshState();
        }

        /// <summary>测试连接（用表单当前值——未保存——探活；结果就地回显在按钮左侧胶囊，不用翻日志）。</summary>
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
                {
                    TestState = "ok";
                    TestText = "探活在线 · " + r.LatencyMs + " ms";
                    ServiceHub.Host.Info("连接成功：" + ep.BaseUrl + "\r\n响应 " + r.LatencyMs + " ms（协议 " + Protocol + "）", "测试连接");
                }
                else
                {
                    TestState = "fail";
                    TestText = "不可达 · " + r.Error;
                    ServiceHub.Host.Info("连接失败：" + r.Error + "\r\n检查服务地址/协议是否匹配部署形态。", "测试连接");
                }
            }
            catch (Exception ex)
            {
                TestState = "fail";
                TestText = "不可达 · " + ex.Message;
                ServiceHub.Host.Info("连接失败：" + ex.Message, "测试连接");
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

        /// <summary>应用推理参数（落 INI + 同步写回当前引擎卡 + HTTP 引擎热重载重探活）。</summary>
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

            // 卡参数同步：现场改过的地址/模型名记住（下次切走再切回即恢复，不用重填）
            var card = EngineRegistry.Find(cfg.ActiveEngineId, ServiceHub.EngineCards);
            if (card != null)
            {
                card.Protocol = cfg.Protocol;
                card.HttpBaseUrl = cfg.HttpBaseUrl;
                card.HttpModel = cfg.HttpModel;
                card.ApiKey = cfg.ApiKey;
                card.HttpTimeoutMs = cfg.HttpTimeoutMs;
                ServiceHub.EngineCards.SaveParams(card);
                ReloadCards();   // 卡片底部"服务参数"小字即时更新
                RefreshState();
            }

            // Mock 数据模式：参数只落盘/写卡，不热重载演示引擎（关闭 Mock 后按此配置连接）
            if (ServiceHub.MockMode)
            {
                ServiceHub.Host.Info("推理参数已保存。当前为 Mock 数据模式，关闭后按此配置连接真实引擎。", "已保存");
                return;
            }

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
                ServiceHub.Host.Info("推理参数已保存。切换到 HTTP 型引擎时按此配置连接。", "已保存");
            }
        }
    }
}
