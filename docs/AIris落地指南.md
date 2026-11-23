# AIris（AI 智瞳）落地指南：模型接入与后续开发

> 定位：本文件替代原《AIris 调研报告》，只保留**能指导开工的事实**——模型怎么接、
> 服务端怎么部署、剩下哪些工作、每项怎么验收。
> 出处说明：原调研报告已删除，其结论并入本指南；主仓源码注释已统一改指本指南章节，
> `ThirdParty\Kaleidoscope` 内两条历史注释仍标注「调研报告 §10.2」（指原报告章节，
> 等效契约见本指南 §2.3，随下次子模块改动一并清理）。
> 最后核对：2026-11-23（V0.1.11 现状，代码事实以本文引用的源码文件为准）。

---

## 一、一句话现状

平台的**软件侧已全功能就绪**：6 页 UI、配方/工步/判定/审计全链路、Mock 三件套可无硬件
跑通 7 步全流程；检测引擎与真相机都留了唯一接入面——
**现在缺的不是架构，而是「服务端部署 + 真机硬件到位」这两件外部事**。

```
┌────────────────────────────────────────────────────────────┐
│ WPF 宿主（AIris.WPF）：监控台/配方/引擎/审计/设备 6 页 UI      │
├────────────────────────────────────────────────────────────┤
│ RunMonitor（编排线程：采集 → 检测 → 判定 → 工步 → 审计）      │
│   ├─ JudgeEngine    ROI-IoU × 数量 × 稳定帧（确定性、可审计）  │
│   └─ WorkstepEngine 状态机（OK 推进 / NG 挂起 / 重检）        │
├───────────────────────┬────────────────────────────────────┤
│ IDetectionEngine ✓留桩 │ ICameraSource ✓留桩                 │
│  └ HttpDetectionEngine │  └ HikCameraSourceStub（占位）       │
│     └ Kaleidoscope.Infer（四协议门面）│  └ KxHik2DCamera（库内现成）│
├───────────────────────┴────────────────────────────────────┤
│ Mock 三件套（演示/回归专用，永不删除）                        │
│  MockScene · MockCameraSource · MockDetectionEngine         │
└────────────────────────────────────────────────────────────┘
```

红线（违反即返工，详见 AGENTS.md）：
- 真实接入 = 实现 `IDetectionEngine`（或填 `HttpDetectionEngine`），**禁止绕过接口**在页面/VM 直接调模型；
- 检测框**永不显示置信度**（模型无分数输出），位置判定只走 ROI-IoU 几何规则 + 连续 N 帧稳定；
- 全本地离线部署（云端 API 只能作为 POC 对照组，出货形态必须是本地服务）。

---

## 二、模型接入（当前最关键路径）

### 2.1 选型与许可证（决定能不能卖）

| 模型 | 许可证 | 定位 | 显存/性能参考（RTX 5070 Ti 16GB） |
|---|---|---|---|
| **Qwen3-VL-8B** | **Apache 2.0 ✅ 商用合规** | **当前商用主力（引擎页默认卡）** | 8.8B dense；**BF16 权重约 16~17GB，16GB 卡跑不动**，必须用官方 FP8 权重（约 9GB）单帧约 480ms（batch=1） |
| **Qwen2.5-VL-7B** | **Apache 2.0 ✅ 商用合规** | **上一代对照（保留退路）** | BF16 约 14GB，16GB 卡原精度可跑；面板/指令跟随弱于 Qwen3，不接受量化的现场用它 |
| LocateAnything-3B | 仅限研究，**禁商用**（底座 Qwen2.5-3B 也是 Research License，双重非商用） | **仅 POC 摸效果上限** | 单帧约 430ms；la_flash 峰值约 11.7GB；locateanything-batch 批处理 61ms/帧（16 FPS @batch≈32） |
| Qwen2.5-VL-3B | Qwen Research License | **不要用**（3B 版不是 Apache） | — |
| Grounding DINO / Florence-2 / OmDet-Turbo | Apache 2.0 / MIT | 备选（专用检测，语义判断能力弱） | 成熟 |
| YOLO-World | GPLv3 | 仅限工厂内部自用（卖软件有传染风险） | — |

结论：**POC 可用 LocateAnything 验证效果，出货默认切 Qwen3-VL-8B（FP8）；不接受量化的现场切 Qwen2.5-VL-7B 原精度**。
两家均为 Apache 2.0，卡上许可证胶囊如实标注（研究许可红/商用绿）。
模型输出口径全链路统一为 **0~1000 归一化框**（左上/右下整数），与 ROI 归一化天然同源；
Qwen3-VL 原生输出 `bbox_2d` 同口径，`InferClient`/`BoxParser` 零改动。

### 2.2 接入架构（写代码前先看懂这四层）

```
业务层  RecipeEditor / MonitorPage / RunMonitor
   │    只看 IDetectionEngine（AIris.Core\Detection\IDetectionEngine.cs）
适配层  HttpDetectionEngine（AIris.Core\Detection\HttpDetectionEngine.cs）
   │    职责：EngineConfig → InferEndpoint、JPEG 编码、失败降级、日志节流（同因 5s 一条）
   │    探活：Load() 内同步 ProbeAsync（超时从紧 ≤3s），失败不抛 → IsLoaded=false → UI 显示回落 Mock
协议层  Kaleidoscope.Infer（submodule：ThirdParty\Kaleidoscope\Kaleidoscope\Infer\）
   │    InferClient（门面）→ InferTransportBase（信封差异）
   │      ├ OpenAiTransport     /v1/chat/completions
   │      ├ AnthropicTransport  /v1/messages
   │      ├ OllamaTransport     /api/chat
   │      └ CustomJsonTransport POST /detect（框直出）
   │    回包解框：BoxParser 双策略（JSON bbox_2d / <box> 文本标签），钳位 0~1000
服务层  推理服务（vLLM / llama-server / Ollama / 自研 FastAPI）
```

「换模型/换框架只改下拉框」的实现就在这一层：宿主与业务零改动；
多模型并存时再加一层**引擎卡**（`EngineRegistry` 内置 4 卡 + `EngineCardStore` 自定义卡，
每卡独立连接参数，切卡即回填）——见 §2.8。

### 2.3 四协议对照（引擎页下拉 → 服务端部署形态）

| 协议（EngineConfig.Protocol） | 服务端 | 探活端点 | 什么时候用 |
|---|---|---|---|
| `openai`（OpenAI 兼容） | vLLM / llama.cpp server / LMDeploy / Ollama 兼容层 | `GET /v1/models` | **推荐主力**：Qwen2.5-VL-7B 用 vLLM 起，一行启动参数 |
| `ollama`（原生） | `ollama serve` | `GET /api/tags` | 单机试跑/低配验证（注意 Ollama 拉 Qwen2.5-VL 需 7B 版） |
| `anthropic` | 官方/兼容网关 | 无（配置齐全即视为就绪，连通性由首检验证） | 离线现场**不用**，只作 POC 对照 |
| `custom`（自研 FastAPI） | 自研服务 `POST /detect` | `GET /health` | 需要框直出/批量优化（locateanything-batch）时用 |

**自研 CustomJson 契约**（CustomJsonTransport 与 BoxParser 已按此实现，服务端照抄即可）：

```jsonc
// POST http://<host>:<port>/detect
// 请求
{ "model": "qwen2.5-vl-7b", "prompts": ["金属垫板", "散热器"], "image_b64": "<JPEG base64>" }
// 响应（坐标 0~1000 归一化，左上/右下）
{ "ok": true, "detections": [ { "label": "金属垫板", "x1": 62, "y1": 112, "x2": 492, "y2": 532 } ] }
// 失败：{"ok": false, "error": "..."}；无目标：{"ok": true, "detections": []}
```

**grounding prompt 已在协议层内置**（`InferClient.BuildGroundingPrompt`，三句定格式：
列目标 → 只输出 JSON 数组 `[{"bbox_2d":[x1,y1,x2,y2],"label":"…"}]` → 坐标 0~1000
左上原点）。自定义服务若要贴合 Qwen 原生输出，
也可直接返回文本让 BoxParser 解析（两种形态都收）。

### 2.4 引擎页配置项对照（现场怎么填）

> 引擎页是**卡片制**：内置 4 张卡（Mock / LocateAnything-3B / Qwen2.5-VL-7B / Qwen3-VL-8B），
> 每张卡**独立保存**下面这套连接参数（落 `Data\EngineCards.json`），切换即回填表单；
> 「＋ 添加引擎」可追加自定义卡（见 §2.8）。下表字段 = 当前选中卡的参数。

| 界面字段 | EngineConfig 字段 | 说明 |
|---|---|---|
| 推理协议 | `Protocol` | 上表四值之一；未知值回退 custom |
| HTTP 推理服务地址 | `HttpBaseUrl` | 如 `http://127.0.0.1:8000`（不带端点路径） |
| 服务模型名 | `HttpModel` | vLLM 的 `--served-model-name`；Ollama 的模型 tag |
| API Key | `ApiKey` | 本地服务留空；OpenAI/Anthropic 网关才填 |
| 超时 | `HttpTimeoutMs` | 默认 5000ms（单帧 430~480ms 量级，留足余量） |
| 批处理大小 | （自研服务端参数） | 界面展示用；batch 越大吞吐越高，延迟也越高 |
| 解码模式 | （LocateAnything 三档） | `hybrid`（MTP 优先，默认）/ `fast`（MTP）/ `slow`（AR） |

### 2.5 接入步骤（照着做即可验收）

1. **起服务端**（示例：vLLM + Qwen3-VL-8B，OpenAI 兼容口）
   ```bash
   # Linux + CUDA（vLLM 以 Linux 为主；Windows 现场用 WSL2 或 Linux 迷你主机）
   # 【显存红线】Qwen3-VL-8B BF16 约 16~17GB，16GB 卡必须用官方 FP8 权重（约 9GB）：
   vllm serve Qwen/Qwen3-VL-8B-Instruct-FP8 --served-model-name Qwen/Qwen3-VL-8B-Instruct \
       --port 8000 --limit-mm-per-prompt image=1 --max-model-len 8192
   # 16GB 卡替代方案：Qwen3-VL-4B BF16（约 8GB）或 Qwen2.5-VL-7B BF16（约 14GB 原精度）
   # 离线现场：模型权重提前落盘，HF_HUB_OFFLINE=1
   # vLLM 版本要求 ≥0.11.0（Qwen3-VL 架构支持；旧版会报 unknown model）
   ```
2. **引擎页**选引擎卡（或「＋ 添加引擎」新建）→ 选协议 `openai` → 填地址/模型名 → 点「测试连接」（= ProbeAsync）
   出现「探活在线（xx ms）」即通；失败看就地胶囊的原因（地址/端口/模型名/显存）。
3. **切换引擎** → 确认弹窗（显示该卡协议/地址/模型名）→ `IsLoaded=true`，监控台右上「Mock 演示引擎」标签变真实引擎名。
4. **配方 prompts 来源**：工步 `ExpectedPart` 的自然语言（如「金属垫板」「M3 盘头螺丝」），
   每帧随工步变化传给检测引擎，无需重启服务。
5. **验收清单**：
   - 监控台单帧耗时显示真实推理耗时（非 0ms）；运行日志出现 `[<引擎名>] openai 协议探活 …`；
   - 故意断开服务 → 信息行出现「未接入，检测回落 Mock」，日志 5s 节流不刷屏；
   - 审计记录 `EngineName` 字段如实写真实引擎名（不是 Mock）；
   - 判定结果与 Mock 口径一致（ROI-IoU × 数量 × 稳定帧），无置信度字段泄漏到 UI；
   - 切走再切回该卡：协议/地址/模型名自动回填（不用重填）。

### 2.6 性能预算（装配节拍视角）

- 装配是**秒级人工节拍**，单帧 430~480ms 的 3B/7B/8B 量级都绰绰有余（16 FPS@batch32 为
  LocateAnything-3B + locateanything-batch 实测，7B/8B 以 batch=1 单帧为准）；
- 检测线程节拍 300ms（`RunMonitor.DetectIntervalMs`），JPEG 编码开销未实测，
  按 1080p 量级预留 10~20ms/帧；
- 多工位并行 = 多服务实例/多卡，不要在一个服务里串行排多路（延迟叠加）；
- 显存：Qwen3-VL-8B BF16 权重约 16~17GB（**16GB 卡跑不动**），必须用官方 FP8 权重
  （约 9GB，加 KV cache 按 12~13GB 预留；FP8 需要 Ada 及以上架构，5070 Ti 支持）；
  Qwen2.5-VL-7B BF16 约 14GB，加运行时按 15~16GB 预留（5070 Ti 16GB 单卡 batch=1 可跑，
  上批处理需降 batch 或换 24GB 卡）；Qwen3-VL-4B BF16 约 8GB（16GB 卡的安全余量选择）；
  LocateAnything-3B 实测 la_flash batch=4 峰值约 11.7GB（sdpa 后端约 35GB，必须 flash 后端）。

### 2.7 已知坑（先知道再踩）

| 坑 | 表现 | 对策 |
|---|---|---|
| 模型无置信度 | 无法按分数过滤误检 | 判定只用 ROI-IoU + 数量 + 稳定帧；误检靠 POC 调 prompt/光源 |
| 小零件（M3）/相似件/对称件装反 | 检出率不保证 | POC 必测 10 类典型零件；兜底：局部放大二次推理 / few-shot 微调 / 模板匹配双通道（`DualChannel` 已留规则位） |
| 金属反光 | 误检/漏检 | 高角度环形光（60~90°）+ 漫射罩，必要时偏振片（硬件选型走 vision-hardware-selection 流程精算） |
| LocateAnything 官方仅 Linux + BF16 | Windows 直跑不了 | 服务端 Linux/WSL2；WPF 宿主只调 HTTP，不吃模型依赖 |
| Qwen3-VL-8B BF16 超 16GB | 16GB 卡 OOM 起不来 | 用官方 FP8 权重（`Qwen3-VL-8B-Instruct-FP8`，约 9GB）；或换 Qwen3-VL-4B / Qwen2.5-VL-7B |
| vLLM 版本过旧不认 Qwen3-VL | 报 unknown model / 架构不支持 | vLLM ≥0.11.0；老版本先升级镜像 |
| Anthropic 无 health | 探活只能"配置即就绪" | 离线现场本来就不用它，忽略 |
| vLLM grounding 输出不稳定 | 偶尔带 markdown 围栏/多余文字 | BoxParser 已容错（剥围栏、双策略合并），仍不稳定就调 prompt 或换 custom 框直出 |

### 2.8 添加检测引擎（自定义模型接入 · 零改代码）

引擎页右上「＋ 添加引擎」= 接新模型的**第一入口**，不用改代码、不用发版：

1. 填 **引擎名称**（卡片标题，必填、不可与现有卡重名）、**许可证说明**（如实标注，商用前确认）；
2. 选 **推理协议**（§2.3 四档，OpenAI 兼容=vLLM/llama-server 推荐）、填 **服务地址 / 模型名 / API Key**；
3. 点「添加引擎」→ 卡片追加到引擎页末尾（带 🗑 删除按钮）；
4. 点该卡「切换到此引擎」→ 确认 → 探活通过即接上；未接通时检测照旧回落 Mock（流程不中断）。

**落盘与红线**：
- 自定义卡存 `00_ExeBuild\Data\EngineCards.json` 的 `Custom` 段（现场数据，**不入库**、不进部署包）；
  内置卡的参数改动记在 `Overrides` 段（按 id 覆盖，升级版本不丢现场配置）。
- **内置 4 张卡不可删除**（代码内预设）；自定义卡可删，删除不影响模型服务本身。
- 删掉当前正在使用的自定义卡 → 自动回落 Mock，`ActiveEngineId` 同步改写，不会留悬空配置。
- 旧版 INI（单套全局 HTTP 参数）首次启动自动迁移到当前引擎卡（只发生一次），老现场升级无感。

---

## 三、未完成的开发工作（按优先级排列）

> 状态标记：✅ 已完成｜🟡 部分完成｜⬜ 未开始
> 验收口径：改 Core 跑 `Tools\probe_ng_semantic.ps1`；改 UI 跑截图+物理点击；改判定同步本指南。

### 3.1 关键路径（POC 成败在这两项）

| # | 工作 | 状态 | 怎么做 | 验收 |
|---|---|---|---|---|
| 1 | **检测服务端接入** | 🟡 宿主侧全就绪，差服务端 | 按 §2.5 部署 vLLM/Ollama/自研 FastAPI；引擎页配卡切换（新模型走 §2.8「＋ 添加引擎」零改代码） | §2.5 验收清单全过 |
| 2 | **POC 检出率实测** | ⬜ | 5070 Ti 工控机 + 工厂真实工件照片，测 10 类典型零件（大件/小螺丝/反光件/相似件/对称件）；产出检出率报告 | 关键零件检出率 ≥ 工厂可接受阈值（建议与产线共同定），未过不承诺交付 |

### 3.2 硬件接入（POC 通过后按工位推进）

| # | 工作 | 状态 | 怎么做 | 验收 |
|---|---|---|---|---|
| 3 | **真机相机接入** | 🟡 接口留桩（`HikCameraSourceStub`） | 实现 `ICameraSource`，内部包 Kaleidoscope `Cameras/` 的 `KxHik2DCamera`（海康 GigE/USB3）；**画面统一走位图帧**（相机 SDK HWND 与 WPF 分层窗 airspace 互斥，禁止直连 HWND）；曝光/增益参数映射到设备页 | 监控台出真机画面、帧率稳定；断开相机 UI 有明确提示 |
| 4 | **PLC/IO 工位联锁** | ⬜ | Kaleidoscope `PlcNetCommQue`（HslCommunication；授权文件 `HslLicense.dat`/`hsl.dat` 已在 `00_ExeBuild\`）；对接三色灯/声光报警/启停信号；断连重连+后台心跳走库内既有范式（见 hmi-comm-wrapper skill） | 判定 OK/NG 驱动三色灯；断网重连自动恢复 |
| 5 | **引导屏 + 声光报警** | ⬜ | 装配工位大屏显示"当前工步图示 + 下一步"，可与监控台同源（第二阶段做独立大屏页面/第二显示器输出） | 工人按引导完成装配；NG 声光同步触发 |
| 6 | **MES 对接（可选）** | ⬜ | 设备页已有 MES 地址/启用开关；实现 HTTP 上报（判定结果/SN/工步/时间/截图路径），契约按工厂 MES 定 | 判定记录进 MES；断网本地缓存补传 |

### 3.3 软件体验收尾（无硬依赖，随时可做）

| # | 工作 | 状态 | 怎么做 | 验收 |
|---|---|---|---|---|
| 7 | **UI 检测框三态** | 🟡 数据齐、缺样式映射 | 判定结果已有（JudgeEngine）：橙实线=待装目标（dim 底）/ 绿实线=已装确认 / 黄虚线=等待 / 红实线=NG；`KxImageStage` Overlay 按工步状态上色 + 标签胶囊 | 监控台 7 步演示中框色随判定变化 |
| 8 | **配方编制 SOP + 部署包** | 🟡 配方中心可用 | 编制 SOP（拍照 → 画 ROI → 填自然语言 → 试跑校准，目标新产线半天）；离线部署包（模型权重 + 运行时 + 授权文件，全离线安装脚本） | 新配方半天内产出并跑通全流程；离线机断网部署成功 |
| 9 | **多工位形态** | ⬜ | 一套服务多工位 = 多相机 + 多配方按工位绑定；先做单机多相机，再看集中管理 | 两工位并行互不干扰 |
| 10 | **审计/报表增强（按需）** | 🟡 基础可查 | 导出 CSV/PDF、按 SN 追溯、NG 原因统计图表 | 客诉可举证、周报可产出 |

---

## 四、开发与验收命令（照抄可用）

```powershell
# 构建（新增文件漏编时删 AIris.Core\obj+bin 后 -t:Restore,Build 整编）
& "D:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  "AIris.WPF\AIris.WPF.csproj" /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /nologo /v:m

# 冒烟：启动 exe → 主窗「AI 智瞳」→ 顶栏导航切 6 页 → 关闭
Start-Process "00_ExeBuild\AIris.WPF.exe"

# 判定语义回归（改 JudgeEngine/WorkstepEngine 后必跑）
powershell -File Tools\probe_ng_semantic.ps1

# UI 截图目检（明暗两套各看一遍，截图落 Tools\Shots\）
powershell -File Tools\shot_ui.ps1 -pages "monitor,recipe,engine,audit,settings"

# UI 物理点击回归（验 WindowChrome 命中吞点击，静态截图验不出）
powershell -File Tools\click_ui.ps1 -x <x> -y <y>
```

---

## 五、事实来源（改代码前先读这些文件头注释）

| 主题 | 文件 |
|---|---|
| 检测引擎接入面（接口契约 + 归一化口径） | `AIris.Core\Detection\IDetectionEngine.cs` |
| HTTP 适配层（组包/探活/降级节流） | `AIris.Core\Detection\HttpDetectionEngine.cs` |
| 协议门面与解框 | `ThirdParty\Kaleidoscope\Kaleidoscope\Infer\InferClient.cs`、`BoxParser.cs`、`InferEndpoint.cs` |
| 相机接入面 | `AIris.Core\Camera\ICameraSource.cs` |
| 判定语义 | `AIris.Core\Judge\JudgeEngine.cs`（对应本文 §2.7 与 AGENTS 铁律 12） |
| 工步状态机 | `AIris.Core\Engine\WorkstepEngine.cs` |
| 编排线程（采集→检测→判定→审计） | `AIris.Core\Engine\RunMonitor.cs` |
| 配方 JSON 结构（含示例） | `AIris.Core\Model\Recipe.cs` |
| Mock 三件套 | `AIris.Core\Detection\MockScene.cs`、`MockDetectionEngine.cs`、`Camera\MockCameraSource.cs` |
| UI 设计稿（本仓库落地方案） | `docs\ui_design_mockups.html`（C 方案「熔金流场」单方案） |
