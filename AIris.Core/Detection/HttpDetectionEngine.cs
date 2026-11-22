using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIris.Core.Model;
using Kaleidoscope.Infer;

namespace AIris.Core.Detect
{
    /// <summary>
    /// HTTP 推理引擎（V0.1.7 从"桩"升级为 InferClient 薄适配——AGENTS.md"大模型现状"
    /// 的唯一接入面，接缝位置不变，内部实现换沉库协议层）。
    ///
    /// 【干什么】组 InferEndpoint（协议/地址/模型/Key）→ InferClient.DetectAsync
    /// （OpenAI 兼容 / Anthropic / Ollama / 自研 /detect 四协议统一收口）→ 0~1000
    /// 归一化框。换部署形态（vLLM↔llama.cpp↔Ollama↔自研 FastAPI）= 改引擎页下拉框，
    /// 本类与上层零改动。
    ///
    /// 【Load 语义】同步探活（≤2s 上限）成功置 IsLoaded=true；失败不抛——UI 显示
    /// "未接入，检测回落 Mock"（与旧桩口径一致，RunMonitor 降级路径不变）。
    /// 【语义红线】InferBox 无置信度——全链路判定只走 ROI-IoU 几何规则。
    /// </summary>
    public class HttpDetectionEngine : IDetectionEngine
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _license;
        private readonly InferClient _client = new InferClient();

        private EngineConfig _cfg;

        public HttpDetectionEngine(string id, string name, string license)
        {
            _id = id;
            _name = name;
            _license = license;
        }

        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public string LicenseNote { get { return _license; } }

        /// <summary>是否已加载（探活成功；失败只记录不抛，UI 显示降级态）。</summary>
        public bool IsLoaded { get; private set; }
        public EngineStats Stats { get; } = new EngineStats();
        public event Action<string> Log;

        public void Load(EngineConfig cfg)
        {
            _cfg = cfg;
            // 同步探活（检测线程/VM 上下文均可等 2s；失败不抛降级）
            try
            {
                var ep = BuildEndpoint(cfg);
                var probe = _client.ProbeAsync(ep, CancellationToken.None).GetAwaiter().GetResult();
                IsLoaded = probe.Ok;
                OnLog("[" + _name + "] " + cfg.Protocol + " 协议探活 " + ep.BaseUrl + " → "
                    + (probe.Ok ? "在线（" + probe.LatencyMs + "ms）" : "不可达：" + probe.Error));
            }
            catch (Exception ex)
            {
                IsLoaded = false;
                OnLog("[" + _name + "] 探活异常：" + ex.Message);
            }
        }

        public async Task<DetectResult> DetectAsync(Bitmap frame, IList<string> prompts, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_cfg == null || string.IsNullOrEmpty(_cfg.HttpBaseUrl))
                    return Degraded("未配置推理服务地址", sw);

                var ep = BuildEndpoint(_cfg);
                var r = await _client.DetectAsync(ep, EncodeJpg(frame), prompts, ct).ConfigureAwait(false);
                if (!r.Ok) return Degraded(r.Error, sw);

                sw.Stop();
                Stats.TotalCalls++;
                Stats.LastElapsedMs = sw.ElapsedMilliseconds;
                var dets = new List<Detection>();
                foreach (var b in r.Boxes)
                    dets.Add(new Detection { Label = string.IsNullOrEmpty(b.Label) ? "" : b.Label, X1 = b.X1, Y1 = b.Y1, X2 = b.X2, Y2 = b.Y2 });
                return new DetectResult { Ok = true, Detections = dets, ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return Degraded("检测异常：" + ex.Message, sw);
            }
        }

        /// <summary>EngineConfig → InferEndpoint（协议字符串映射；未知值回退 custom 契约）。</summary>
        private static InferEndpoint BuildEndpoint(EngineConfig cfg)
        {
            InferProtocol p;
            switch ((cfg.Protocol ?? "").ToLowerInvariant())
            {
                case "openai": p = InferProtocol.OpenAi; break;
                case "anthropic": p = InferProtocol.Anthropic; break;
                case "ollama": p = InferProtocol.Ollama; break;
                default: p = InferProtocol.CustomJson; break;
            }
            return new InferEndpoint
            {
                Protocol = p,
                BaseUrl = cfg.HttpBaseUrl,
                Model = cfg.HttpModel,
                ApiKey = cfg.ApiKey,
                TimeoutMs = cfg.HttpTimeoutMs,
            };
        }

        /// <summary>降级结果（UI 提示引擎未接入，检测链回落 Mock）。</summary>
        private DetectResult Degraded(string why, System.Diagnostics.Stopwatch sw)
        {
            sw.Stop();
            OnLog("[" + _name + "] 检测降级：" + why);
            return new DetectResult { Ok = false, Error = why, Detections = new List<Detection>(), ElapsedMs = sw.ElapsedMilliseconds };
        }

        private static byte[] EncodeJpg(Bitmap frame)
        {
            using (var ms = new MemoryStream())
            {
                frame.Save(ms, ImageFormat.Jpeg);
                return ms.ToArray();
            }
        }

        public void Dispose() { _client.Dispose(); }

        private void OnLog(string msg) { var h = Log; if (h != null) h(msg); }
    }
}
