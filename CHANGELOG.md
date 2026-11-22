# AIris（AI 智瞳）CHANGELOG

> 版本史：最新在顶部。commit 前同轮工作只追加 `###` 小节不加版本号。

## V0.1.0（2026-11-22，未提交）

### 项目奠基

- 项目定名：中文「AI 智瞳」/ 英文「AIris」（AI + Iris/虹膜），定位自然语言驱动的通用视觉检测与防错平台，装配防错为首个落地场景；调研报告迁移至 `docs/AIris调研报告.md` 并改写为通用平台口径
- UI 设计稿 `docs/ui_design_mockups.html`（6 页 × 6 弹窗 × 5 方案 × 明暗，33 项核验全 PASS）；用户选定 **C 方案「熔金流场」**（橙粉渐变 #F97316→#EC4899 / 胶囊按钮 / 圆角 16 / 底部 Dock 导航 / 浅色默认）作为落地蓝本
- AGENTS.md 按项目实际情况改写（继承 HuaJiVision 通用纪律：UTF-8 编码 / 构建验证 / 注释规范 / 截图目检 / 共享控件复用 / 改彻底不留兼容包袱；剥离原项目专属内容）
- 技术选型定版：net472 + WPF（SDK 风格 csproj，64 位进程）；Kaleidoscope 库挂 submodule 复用通讯与 UI 控件（`KaleidoscopeUITargetFrameworks=net472` 裁剪）
- 架构定版：AIris.Core（纯逻辑：模型/检测引擎/相机/判定/工步/存储）+ AIris.WPF（MVVM）；检测引擎走 `IDetectionEngine` 唯一接入面（大模型未接入，HTTP 桩 + Mock 三件套演示全流程）

### V0.1.0 软件全功能落地（2026-11-22，同轮）

- **仓库骨架**：AIris.sln + AIris.Core/AIris.WPF 双工程 + Kaleidoscope submodule（.gitmodules + Directory.Build.props 裁剪）+ .gitignore（00_ExeBuild/授权文件/日志不入库）
- **Core 全量**：Recipe/StepRule/ROIRect/AuditRecord/EngineConfig/DeviceConfig 模型；`IDetectionEngine` 接口 + MockDetectionEngine（MockScene 联动出框）+ HttpDetectionEngine（HTTP 桩，组包/解析/降级已就绪，服务端未接入）+ EngineRegistry（三卡）；ICameraSource + MockCameraSource（GDI+ 合成俯拍画面，与 PartLayout 同源坐标）+ HikCameraStub；JudgeEngine（ROI-IoU × 数量 × 整检三类语义）；WorkstepEngine（稳定帧累计/推进/NG 挂起/重检）；RunMonitor（采集→检测→判定→工步→审计 编排线程）；RecipeStore(JSON)+JsonAuditStore(JSONL 按天+截图 PNG)+AppSettings(INI)
- **WPF 全量**：主题双字典（C 方案 Token + 91 键库契约键段，浅色默认深色可切）；MainWindow dock 壳（ChromeBorderlessWindow 派生：渐变顶栏 + 底部胶囊导航 + 页面切换）；6 页全功能（运行监控台：画面叠加+工步引导+NG 告警+演示控制 / 配方中心：卡列表 CRUD+启停互斥 / 配方编辑器：工步序列+自然语言预期零件+判定规则+ROI 数值标画预览 / 检测引擎：三卡切换+推理参数+状态 KPI / 审计追溯：筛选+KPI+记录表 / 设备与集成：设备列表+参数表单）；4 弹窗（新建配方两步向导/通用确认/NG 详情/存档详情）
- **HslCommunication 授权部署**：`HslLicense.dat` + `hsl.dat` 从 `E:\Project\HslCommunicationLicense` 拷入 `00_ExeBuild\`（PLC 通讯走 Kaleidoscope PlcNetCommQue 时的授权前提；两文件已在 .gitignore，永不入库）
- **验证闭环**：整编 0 error；冒烟主窗「AI 智瞳」正常；Core 探针全绿（配方落库启用/Mock 出框/判定 OK-NG 语义/稳定帧推进/审计落档查询）；PrintWindow 五页目检（监控台含 NG 告警链路截图、配方中心、引擎页、审计页 10 条真实 NG 记录、设备页）全部与设计稿 C 方案观感对位
- **实踩坑沉淀**（均已修复并回写 AGENTS.md）：①MonitorPage 第三列误写 `Width="1"`（1 像素）而非 `1*`（比例）→ 右列消失；②页面覆写 TextBox 垂直 Padding 致 FreshInput 的 PART_ContentHost 文本被裁 → 全部归零（样式默认垂直 0 + VerticalContentAlignment 居中）；③EnginePage 缺 `InvBoolToVis` 资源声明 → 切页即崩；④初始页 OnEnter 需 LoadRecipe 后手动 TouchCurrent 触发；⑤PS1 无 BOM 写中文必 GBK 乱码（铁律 1 现世报应）
- **回归探针**：`Tools\probe_ng_semantic.ps1`（NG 语义）、`Tools\shot_ui.ps1`（PrintWindow 页面目检，页面 id 逗号分隔）

