using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIris.Core.Model;
using Newtonsoft.Json;

namespace AIris.Core.Detect
{
    /// <summary>
    /// HTTP 推理引擎（**桩**，大模型接入的唯一填实点——AGENTS.md"大模型现状"）。
    ///
    /// 未来形态（调研报告 §4 已定）：Linux 工控机常驻推理服务（locateanything-batch 的
    /// FastAPI / Qwen2.5-VL 的 vLLM/LMDeploy），本类组 HTTP 请求调用。
    ///
    /// 现在已实现的部分：配置装载、图像编码（JPG base64）、请求组包、响应解析（0~1000 归一化框）、
    /// 超时/降级（服务不可达 → Ok=false，不抛异常）。**未实现**：服务端还没有——任何调用都会
    /// 走"服务不可达"降级，UI 显示"引擎未接入，当前 Mock 演示"。
    ///
    /// 接入指引（填实者读这里）：与推理服务约定一个 POST /detect 契约即可，请求体：
    /// { "model": "...", "prompts": ["4 颗 M3 盘头螺丝"], "image_b64": "..." }
    /// 响应体：
    /// { "ok": true, "detections": [ {"label":"M3 盘头螺丝","x1":690,"y1":300,"x2":750,"y2":360} ] }
    /// （坐标 0~1000 归一化；响应不含置信度——语义红线）。改造点在 DetectAsync 内标注 TODO-REAL。
    /// </summary>
    public class HttpDetectionEngine : IDetectionEngine
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _license;
        private readonly HttpClient _http;

        private EngineConfig _cfg;

        public HttpDetectionEngine(string id, string name, string license)
        {
            _id = id;
            _name = name;
            _license = license;
            // net472 HttpClient：连接池按服务端常驻形态配置
            _http = new HttpClient();
        }

        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public string LicenseNote { get { return _license; } }

        /// <summary>加载 = 尝试连通服务；失败只记录不抛（切换引擎确认后 UI 展示降级态）。</summary>
        public bool IsLoaded { get; private set; }
        public EngineStats Stats { get; } = new EngineStats();
        public event Action<string> Log;

        public void Load(EngineConfig cfg)
        {
            _cfg = cfg;
            _http.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, cfg.HttpTimeoutMs));
            // TODO-REAL(接入点 1)：Load 时可调服务 /health 探活，成功置 IsLoaded=true。
            // 当前恒 false —— 引擎页显示"未接入"，检测走降级（RunMonitor 自动回落 Mock 演示）。
            IsLoaded = false;
            OnLog("[" + _name + "] HTTP 引擎桩已装载：BaseUrl=" + cfg.HttpBaseUrl + "（服务端未接入，IsLoaded=false）");
        }

        public async Task<DetectResult> DetectAsync(Bitmap frame, IList<string> prompts, CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // TODO-REAL(接入点 2)：填实以下段——组包→POST→解析框数组。
            // 当前直接降级返回（Ok=false），RunMonitor 捕获后回落 Mock 出框，演示不中断。
            try
            {
                if (string.IsNullOrEmpty(_cfg == null ? null : _cfg.HttpBaseUrl))
                    return Degraded("未配置推理服务地址", sw);

                var body = new
                {
                    model = _cfg.HttpModel,
                    prompts = prompts,
                    image_b64 = EncodeJpg(frame)
                };
                var json = JsonConvert.SerializeObject(body);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await _http.PostAsync(_cfg.HttpBaseUrl.TrimEnd('/') + "/detect", content, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    var text = await resp.Content.ReadAsStringAsync();
                    var r = JsonConvert.DeserializeObject<HttpDetectResponse>(text);
                    if (r == null || !r.ok) return Degraded(r == null ? "响应为空" : (r.error ?? "服务返回失败"), sw);
                    var dets = (r.detections ?? new List<HttpDet>()).Select(d => new Detection
                    {
                        Label = d.label, X1 = d.x1, Y1 = d.y1, X2 = d.x2, Y2 = d.y2
                    }).ToList();
                    sw.Stop();
                    Stats.TotalCalls++;
                    Stats.LastElapsedMs = sw.ElapsedMilliseconds;
                    return new DetectResult { Ok = true, Detections = dets, ElapsedMs = sw.ElapsedMilliseconds };
                }
            }
            catch (Exception ex)
            {
                return Degraded("服务不可达：" + ex.Message, sw);
            }
        }

        /// <summary>降级结果（UI 提示引擎未接入，检测链回落 Mock）。</summary>
        private DetectResult Degraded(string why, System.Diagnostics.Stopwatch sw)
        {
            sw.Stop();
            OnLog("[" + _name + "] 检测降级：" + why);
            return new DetectResult { Ok = false, Error = why, Detections = new List<Detection>(), ElapsedMs = sw.ElapsedMilliseconds };
        }

        private static string EncodeJpg(Bitmap frame)
        {
            using (var ms = new MemoryStream())
            {
                frame.Save(ms, ImageFormat.Jpeg);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public void Dispose() { _http.Dispose(); }

        private void OnLog(string msg) { var h = Log; if (h != null) h(msg); }

        // —— HTTP 契约模型（与服务端约定一致；坐标 0~1000 归一化、无置信度） ——
        private class HttpDetectResponse { public bool ok; public string error; public List<HttpDet> detections; }
        private class HttpDet { public string label; public int x1; public int y1; public int x2; public int y2; }
    }
}
