# AIris（AI 智瞳）CHANGELOG

> 版本史：最新在顶部。commit 前同轮工作只追加 `###` 小节不加版本号。

## V0.1.2（2026-11-22）品牌图标定版换全：C 方案「鸢尾绽放」（用户选定）

### 换全三面（exe 资源 / 运行窗口 / 界面品牌位）+ 快捷方式

- **用户从六款候选中选定 C 方案「鸢尾绽放」**（三瓣水滴花 120° 旋转 + 中心瞳点，Iris
  双关的花形表达）；`docs/branding/icons/logo-c-iris-bloom/` 的 7 帧 ICO（16~256）
  拷入 `AIris.WPF\Assets\app.ico`（Assets 目录此前从未落地，本次新建）；
- **① exe 内嵌图标**：csproj 新增 `<ApplicationIcon>Assets\app.ico</ApplicationIcon>`；
  验证：`ExtractAssociatedIcon` 提取 exe 图标 = 鸢尾绽放 ✓（资源管理器/桌面快捷方式/
  任务栏固定显示此面）；
- **② 运行窗口图标（任务栏运行态/Alt-Tab/窗口标题栏）**：MainWindow 根元素新增
  `Icon="/Assets/app.ico"` + csproj `<Resource Include="Assets\app.ico">`——
  【坑】ApplicationIcon 只管 exe 文件图标，**不会自动成 WPF Resource**，缺 Resource
  Include 则 XAML 的 Icon 运行时加载失败；启动实检任务栏 32px 帧显示鸢尾绽放 ✓；
- **③ 顶栏品牌位**：原「Border 半透白底 + ◉ 占位字符」换成鸢尾绽放**同源矢量**
  （Viewbox + Canvas 128：三 Path 各带 120° RotateTransform + 中心 Ellipse r7，
  与转换前 SVG 完全同几何）——去半透垫、白花直接嵌顶栏渐变（对齐 app icon
  「渐变底+白符号」口径，随 Viewbox 无损缩放）；截图目检 26px 白花清晰 ✓；
- **桌面快捷方式**：新建「AI 智瞳.lnk」指向 00_ExeBuild exe（图标继承 exe 内嵌）；
- 弹窗无需逐个设图标：库 ChromeDialogWindow 标题条走 TitleIconChar 设计字形、
  不读 Window.Icon 且弹窗不上任务栏/Alt-Tab；
- **换标经验沉 ui-mockup-studio logo-playbook 第 4 节「换图标=换全」三面清单**
  （exe≠窗口图标两件事 / Resource Include 坑 / 图标缓存重建 / 品牌位占位符必换）；
- **顺带修复：README 架构图 GitHub 渲染异常**——mermaid `subgraph AIris.WPF（MVVM 壳）`
  未引号+全角括号 = parse error（本地 mermaid 10.9.8 对照截图实锤 Syntax error 炸弹图）；
  修复 = subgraph 改 `id["label"]` 形式 + 全部节点 label 引号化；
- **设计资产沉淀**：六款候选 SVG + 选款预览页（preview.html 手写语义版 /
  logo_preview.html 脚本生成版）+ 全款转换资源（icons/ 每款 PNG 512~16 阶梯 + 7 帧 ICO）
  全部落 `docs/branding/`；转换链路已沉 ui-mockup-studio skill V1.4.0（svg2res.py）。

## V0.1.1（2026-11-22）壳重构：菜单收拢进顶栏 + 三窗钮 + README + 控件复用纪律沉淀

### 顶栏两 bug 返工：点击无响应 + 胶囊形状（用户实点反馈）

- **顶栏按钮点击全无响应（根因：WindowChrome 命中区吞点击）**：ChromeBorderlessWindow 的
  WindowChrome.CaptionHeight 区域内，未设 `IsHitTestVisibleInChrome` 的元素点击一律被吞成拖窗
  ——底部 dock 时代按钮在命中区外所以正常，收拢进 52px 顶栏后导航/🌓/三窗钮全哑。修复：
  `CaptionHeight=52`（顶栏全高拖动/双击最大化）+ 顶栏全部交互元素逐个
  `shell:WindowChrome.IsHitTestVisibleInChrome=True`；原 DragMove 事件变死代码删除，
  glyph 同步改走 StateChanged 事件（覆盖"双击顶栏空白"系统最大化通道）；
- **胶囊形状"太尖锐"返工（形状归库口径）**：上轮自绘 `CornerRadius=99` 纯药丸（两端半圆尖弧）
  被否；核实库 `KxLangButton` 与 HJVision 主窗顶栏全钮均为 **CornerRadius=7** 圆角矩形——库形状
  没问题，问题在自绘未复用。AIris 侧改 `BasedOn="{StaticResource KxLangButton}"` 派生
  （AiNavCapsule）：形状/Padding/字号/Cursor 全库值，配色因顶栏橙粉渐变宿主化（未选中白字/
  hover 白 20%/选中白底+主色字反转），RadioButton+GroupName="DockNav" 天然互斥；顶栏静态
  胶囊（引擎/相机在线）CornerRadius 同步 99→7；
- **验证升级：物理鼠标实点回归**（PrintWindow 只能验静态、验不出点击）——新增
  `Tools\click_ui.ps1`（SetCursorPos+mouse_event 物理点击，与真实鼠标同输入路径）+
  agent-desktop 截图链路：菜单五项切换/三窗钮双向切换（glyph 跟随）/主题深浅切换全部实点通过；
- **顺带修复（深色目检抓到的 V0.1.0 遗留）**：审计页 ListView 文字未绑主题键
  （DisplayMemberBinding 默认前景黑）深色下不可见 → ListView `Foreground=ThemeTextFg` +
  GridViewColumnHeader 轻量样式（透明底+主题字色+底线，去 Aero2 白 chrome）+ ListViewItem
  轻量模板（去默认蓝 chrome，hover/选中=ThemePrimaryDim）；【铁坑】ListViewItem 自定义模板
  必须 `GridViewRowPresenter`（Columns 绑 `GridView.ColumnCollection` 附加属性）——误用
  ContentPresenter 只显示 Content.ToString() 整行变类名，实踩自修。

### 壳重构：菜单收拢进顶栏 + 三窗钮（用户定版）

- **底部 Dock 收拢进顶栏菜单栏**：5 个导航项横排进渐变顶栏（选中=白底胶囊+主色字，未选中=白字 hover 白 20%），内容区上移满屏；
- **胶囊效果重做**：原自绘胶囊带着 WPF 默认焦点虚线框（FocusVisual 未清）观感奇怪——重写 ControlTemplate 并 `FocusVisualStyle={x:Null}`，且改用顶栏反转强调（非白底胶囊浮在彩底上）；
- **右上角三窗钮**：最小化 / 最大化切换（▢↔❐ glyph 跟随）/ 关闭，复用库 `KxTitleMin/Max/CloseButton` 样式（hover 内置：关闭红底），View 层 Click 直操 WindowState；
- **README.md 新建**（readme skill 流程：事实摸底→五页新壳截图落 docs/images→骨架落稿→check_readme/img_inventory 双脚本全 PASS）——徽章/快速开始/界面速览图墙/特性/mermaid 架构/大模型接入契约/目录树/文档导航/配置；
- **shot_ui.ps1 坐标更新**：dock 收拢后 navMap 从底部 y=1008 改顶栏 y=26。

### 控件复用纪律沉淀（三层 + 库内多锚点，工具无关）

- **缘由**：主窗导航胶囊漏查库内 `KxLangButton`（RadioButton 胶囊导航形态）自绘 ControlTemplate，还漏清 WPF 焦点虚线框——库里有对应物却没查。用户进一步追问：记忆/宿主 AGENTS.md 都绑定单一工具或单一仓库，换 Agent 工具开发就又失效——所以触发器必须埋进 Kaleidoscope 库本体（随 submodule 走到任何工具、任何宿主）。
- **三层沉淀（文档层）**：
  ① **Kaleidoscope.UI/README.md** 新增「控件复用索引（按场景查）」整节：场景→控件/样式键对照表（窗壳/三窗钮/导航胶囊/Tab/按钮族/输入/状态/卡片/表格/标签/开关/转换器全覆盖）+ 自绘三条件 + grep 检索命令（随库走到每个宿主，人读）；
  ② **Kaleidoscope/AGENTS.md** 铁律 7（新增）：宿主 UI 开工先查索引、库内有对应物直接用/近似形态 BasedOn 再造/确无才自绘且必须登记——AI 视角强制动作（随 submodule 可达）；
  ③ **AIris AGENTS.md** 铁律 10 升级（引用索引 + 自绘三条件）+ 铁律 11 沉本轮 UI 坑（TextBox 垂直 Padding 裁文本 / ComboBox SelectedItem 绑 ComboBoxItem 集合显示空）+ 约定索引表加"开工查库"行；
- **库内多锚点（必经之路层，子仓 V2.3.4，纯文档）**：宿主接入库 UI 的必经动作全是接触点，路标埋进这些文件任何 Agent 工具都绕不开——
  ① 子仓根 **README.md** 新增「宿主项目 UI 接入必读（人 + AI Agent 通用）」节（clone 后第一屏）；
  ② **Themes/ 五字典**文件头注释加路标段（App.xaml 合并字典 + 查键 grep 必开这些文件）；
  ③ **KxShellDefaults / ChromeDialogWindow / ChromeBorderlessWindow** 三类头注释加路标（启动链/建弹窗/建主窗必经）；
- 子仓（kaleidoscope）改动待按 submodule 提交流程推 GitHub，主仓指针下次提交随收。


## V0.1.0（2026-11-22，已推送 a61dc74）

### 项目奠基

- 项目定名：中文「AI 智瞳」/ 英文「AIris」（AI + Iris/虹膜），定位自然语言驱动的通用视觉检测与防错平台，装配防错为首个落地场景；调研报告迁移至 `docs/AIris调研报告.md` 并改写为通用平台口径
- UI 设计稿 `docs/ui_design_mockups.html`（6 页 × 6 弹窗 × 5 方案 × 明暗，33 项核验全 PASS）；用户选定 **C 方案「熔金流场」**（橙粉渐变 #F97316→#EC4899 / 胶囊按钮 / 圆角 16 / 底部 Dock 导航 / 浅色默认）作为落地蓝本
- AGENTS.md 按项目实际情况改写（继承 HuaJiVision 通用纪律：UTF-8 编码 / 构建验证 / 注释规范 / 截图目检 / 共享控件复用 / 改彻底不留兼容包袱；剥离原项目专属内容）
- 技术选型定版：net472 + WPF（SDK 风格 csproj，64 位进程）；Kaleidoscope 库挂 submodule 复用通讯与 UI 控件（`KaleidoscopeUITargetFrameworks=net472` 裁剪）
- 架构定版：AIris.Core（纯逻辑：模型/检测引擎/相机/判定/工步/存储）+ AIris.WPF（MVVM）；检测引擎走 `IDetectionEngine` 唯一接入面（大模型未接入，HTTP 桩 + Mock 三件套演示全流程）

### 软件全功能落地（同轮）

- **仓库骨架**：AIris.sln + AIris.Core/AIris.WPF 双工程 + Kaleidoscope submodule（.gitmodules + Directory.Build.props 裁剪）+ .gitignore（00_ExeBuild/授权文件/日志不入库）
- **Core 全量**：Recipe/StepRule/ROIRect/AuditRecord/EngineConfig/DeviceConfig 模型；`IDetectionEngine` 接口 + MockDetectionEngine（MockScene 联动出框）+ HttpDetectionEngine（HTTP 桩，组包/解析/降级已就绪，服务端未接入）+ EngineRegistry（三卡）；ICameraSource + MockCameraSource（GDI+ 合成俯拍画面，与 PartLayout 同源坐标）+ HikCameraStub；JudgeEngine（ROI-IoU × 数量 × 整检三类语义）；WorkstepEngine（稳定帧累计/推进/NG 挂起/重检）；RunMonitor（采集→检测→判定→工步→审计 编排线程）；RecipeStore(JSON)+JsonAuditStore(JSONL 按天+截图 PNG)+AppSettings(INI)
- **WPF 全量**：主题双字典（C 方案 Token + 91 键库契约键段，浅色默认深色可切）；MainWindow dock 壳（ChromeBorderlessWindow 派生：渐变顶栏 + 底部胶囊导航 + 页面切换）；6 页全功能（运行监控台：画面叠加+工步引导+NG 告警+演示控制 / 配方中心：卡列表 CRUD+启停互斥 / 配方编辑器：工步序列+自然语言预期零件+判定规则+ROI 数值标画预览 / 检测引擎：三卡切换+推理参数+状态 KPI / 审计追溯：筛选+KPI+记录表 / 设备与集成：设备列表+参数表单）；4 弹窗（新建配方两步向导/通用确认/NG 详情/存档详情）
- **HslCommunication 授权部署**：`HslLicense.dat` + `hsl.dat` 从 `E:\Project\HslCommunicationLicense` 拷入 `00_ExeBuild\`（PLC 通讯走 Kaleidoscope PlcNetCommQue 时的授权前提；两文件已在 .gitignore，永不入库）
- **验证闭环**：整编 0 error；冒烟主窗「AI 智瞳」正常；Core 探针全绿（配方落库启用/Mock 出框/判定 OK-NG 语义/稳定帧推进/审计落档查询）；PrintWindow 五页目检（监控台含 NG 告警链路截图、配方中心、引擎页、审计页 10 条真实 NG 记录、设备页）全部与设计稿 C 方案观感对位
- **实踩坑沉淀**（均已修复并回写 AGENTS.md）：①MonitorPage 第三列误写 `Width="1"`（1 像素）而非 `1*`（比例）→ 右列消失；②页面覆写 TextBox 垂直 Padding 致 FreshInput 的 PART_ContentHost 文本被裁 → 全部归零（样式默认垂直 0 + VerticalContentAlignment 居中）；③EnginePage 缺 `InvBoolToVis` 资源声明 → 切页即崩；④初始页 OnEnter 需 LoadRecipe 后手动 TouchCurrent 触发；⑤PS1 无 BOM 写中文必 GBK 乱码（铁律 1 现世报应）
- **回归探针**：`Tools\probe_ng_semantic.ps1`（NG 语义）、`Tools\shot_ui.ps1`（PrintWindow 页面目检，页面 id 逗号分隔）
