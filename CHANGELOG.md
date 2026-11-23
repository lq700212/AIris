# AIris（AI 智瞳）CHANGELOG

> 版本史：最新在顶部。commit 前同轮工作只追加 `###` 小节不加版本号。

## V0.1.12（2026-11-23）全页面与弹窗按设计稿 C 方案「熔金流场」逐页整改 + 明暗双主题目检

### 一、宿主组件体系（新增 AiStyles.xaml）

- 新增 `AIris.WPF/Styles/AiStyles.xaml`：卡片 / 页头 / 按钮五档（Primary/Secondary/Ghost/Danger/DangerGhost，含 Sm 小号）/
  输入框 / 下拉框 / 状态胶囊 / KPI 块 / 开关 / 滑块 / 复选框等全部宿主组件样式；按钮统一
  **半高圆角药丸**（圆角 = 实际高度/2，见第九节 PillRadius 转换器）、
  输入高 34 圆角 10、卡片圆角 16，在 `App.xaml` 的 ThemeLight 之后合并；
- **按钮族替换**：页面与弹窗一律使用 `AiBtn*`，弃用库 `FreshButton`（CornerRadius=4 方角是用户反馈
  "按钮太尖锐"的根因）；破坏性主操作用 `AiBtnDanger`，监控 "拿走一个（NG 模拟）" 用 `AiBtnDangerGhostSm`；
- **复选框 / 开关自绘**：库模板走 `Brush.Shell.*` 契约键（深色下错色），改为 Theme* 键自绘；
  开关未选中轨道用 `ThemeBorder` 实色（深色下否则看不出是开关）；
- **主题键扩充**：`ThemePrimaryHover` / `ThemePrimaryPressed` / `ThemeOnPrimary` / `ThemePrimaryGrad` /
  `ThemeStageFootBg` / `ThemePrimaryBorder`，明暗两套字典同键同值语义。

### 二、顶栏与导航胶囊（MainWindow.xaml）

- 导航胶囊 = Icon + 文字，**去掉前缀圆点**；半高圆角药丸、高 30、Padding 0 14、间距 4、水平居中；
  选中白底橙字、hover 白 16%；
- 状态胶囊高 22、三窗钮 32 高、新自绘 `AiTopCloseButton`（hover 红底药丸）；
- 内容区 Margin `20,16,20,20`；顶栏交互元素全部 `WindowChrome.IsHitTestVisibleInChrome` 放行。

### 三、五页按设计稿重写

- **运行监控台**（data-page="monitor"）：左舞台 `*` + 右列 400 双列布局；舞台脚注（分辨率 / fps / 引擎 / 判定说明）；
  NG 告警卡（红描边 + 声光已触发 chip + 详情/重检）；工步序列按设计稿着色（**当前步橙点 + 浅橙底 + 半粗**、
  已完成绿 ✓、未到灰 ○）；运行日志自动滚底；底部演示控制条；检测框三态上色（待补蓝 / OK 绿 / NG 红）；
- **配方中心**（recipe）：4 列卡片网格 + 虚线引导卡哨兵（`RecipeCardVm.IsAddTile` + `CreateAddTile`，
  空位引导"新建配方"）；
- **检测引擎**（engine）：3 列引擎卡（当前引擎橙描边 + 许可证 chip）+ 推理参数 + 服务状态 KPI +
  **就地「测试连接」反馈**（不再弹窗打断，`EngineViewModel` 状态行直显）；
- **审计追溯**（audit）：日期 / 判定筛选 + 4 KPI + Grid 自绘表格（GridViewRowPresenter 按列渲染）+ 空态；
- **设备与集成**（settings）：左 236 设备列表 + **活连接状态点**（`DeviceItem : ViewModelBase` +
  `RefreshStatus()`，绿/橙/灰随实际连接与 Mock 态刷新）+ 右连接参数 + Mock 动作条；
- **配方编辑器**（editor，设计稿 `[vNext]` 必须项落地）：左 236 工步序列 + 中配置/判定 + 画布 440 +
  **300px 右栏 ROI 清单**（旧版清单在画布下方）。

### 四、五弹窗统一整改

- `ConfirmDialog` / `ShotDialog` / `NewRecipeDialog` / `NgDetailDialog` / `RoiFullscreenDialog` 统一口径：
  根底 `ThemePanelBg`（深色跟随宿主）、按钮 `AiBtn*` 药丸、底部按钮区 **上方 1px 分隔线 + 12 上内距 + 右对齐**、
  内容区不再重复标题条标题（标题条已示"存档详情 · SN"/"NG 详情 · 工步 N"）；
- 新建配方向导：按设计稿补**步骤指示器**（18px 圆 + 编号 + 26px 连接线；当前步橙底白字加粗，
  未到步面板底灰字），两步切换联动；
- 存档详情 / NG 详情：所有信息行 `TextWrapping="Wrap"`（长文件名/规则快照不再被裁），
  判定色从 `Brushes.OrangeRed/SeaGreen` 改为 `ThemeNg/ThemeOk`。

### 五、行为与数据修复

- **编辑器进入即自动出画面**：`RecipeEditorViewModel.OnEnter` 置 `PreviewOn=true`
  （旧版要手动点"启动取像"，首屏一片网格，标画入口像坏了）；`StartPreview` 内部判运行态，
  监控检测在跑时复用同源帧，不双开相机；
- **页面 OnLeave 对称收尾**：`PageViewModelBase` 新增 `OnLeave()`，`MainViewModel` 切页时先旧页
  `OnLeave` 再新页 `OnEnter`；编辑器离开即停预览取像（不留空转相机线程与订阅）；
- **空场不再清帧**：`MonitorViewModel.OnFrame` / `RecipeEditorViewModel.OnPreviewFrame` 空场出帧时
  保留上一帧（`img ?? FrameImage`）——透明 Mock 帧叠舞台网格就是"空台面"真实观感，
  "等待画面…"只留给真正无帧/断流；
- **审计截图 flatten 到不透明台面**：`RunMonitor.FlattenForArchive` 把透明 Mock 帧铺到
  `#E8EDF5` + 64px 网格台面再落盘（旧版透明 PNG 在弹窗里一片空白，"缺件 NG"失去追溯价值；
  真实相机帧不透明会整幅覆盖，无副作用）；
- **审计查询倒读修复**：`JsonAuditStore.Query` 改文件尾→头倒读，**最新记录在前**；满 `maxRows=500`
  时截**最旧**而不是最新（旧实现正读 + 截尾，导致新记录永远查不到）。

### 六、验证

- MSBuild `AIris.WPF` 0 error（Debug / AnyCPU，64 位）；
- **明暗双主题全页目检**：运行监控台 / 配方中心 / 配方编辑器 / 检测引擎 / 审计追溯 / 设备与集成，
  对照设计稿 C 方案逐项核过（橙粉渐变顶栏、胶囊导航、大圆角卡片、浅色默认）；
- **弹窗明暗目检**：新建配方向导两步（步骤指示器切换正常）、存档详情、NG 详情、ROI 全屏标画、
  保存成功提示；截图区台面网格可见、按钮区上分隔线到位、标题不重复；
- **物理点击回归**：顶栏 6 导航胶囊、主题切换钮、三窗钮（最小化 / 最大化-还原 / 关闭）、
  编辑器进入自动取像（按钮显"停止取像"）与离开停取像（日志"[预览] 编辑页取像关闭"）全部实测通过；
- 审计页新记录生成后倒序显示在首行，"⋯"存档详情弹窗截图带台面网格。

### 七、清理

- 删除 `Tools/Shots/` 下 210 张调试中间截图，保留被 git 跟踪的 18 张文档配图与基准图。

### 八、屏幕适配（DPI / 窗口比例 / 复用不变形）

- **新增 `AIris.WPF/app.manifest`（Per-Monitor V2 DPI 感知）**：`csproj` 以
  `<ApplicationManifest>` 引用。原工程没有清单，进程是 System DPI Aware——单屏 100% 正常，
  但把窗口拖到 125%/150% 缩放的显示器、主副屏 DPI 不同、或远程会话（向日葵/RDP）时，
  Windows 会对整窗做**位图拉伸**，胶囊圆角、文字、1px 描边全部发虚/变形（"胶囊变尖"的
  环境侧根因）。实测进程 awareness 由 1 变 2（PerMonitorV2），WPF 按 DIP 矢量重绘；
- **胶囊基类改自适应高度**：`AiBtn`/`AiBtnSm`/`AiBtnGhostSm`/`AiInput`/`AiCombo`/`AiChip`/
  `AiSwitch`/`AiSlider` 与顶栏导航胶囊、状态胶囊、三窗钮、主题钮全部 `Height` → `MinHeight`。
  任何 DPI/字体缩放/布局挤压下胶囊只会整体变高，不会被固定行高裁掉内容或压扁
  （圆角如何跟着高度自适应见第九节）；
- **去掉 `SnapsToDevicePixels`**（AiBtn 模板 + 导航胶囊模板各一处）：它会把圆角强制按设备
  像素切台阶，非整数缩放（125%/150%）下正是"胶囊边缘发毛、看着尖"的直接原因；主窗与
  5 个弹窗（Confirm/Shot/NewRecipe/NgDetail/RoiFullscreen）统一加 `UseLayoutRounding="True"`，
  布局对齐设备像素但圆角保持抗锯齿；
- **页面内显式固定高度同步清理**：审计"查询"按钮、演示零件下拉、NG 卡"声光报警已触发"
  徽章、日志 OK/NG 徽章、引擎"测试连接"结果徽章均改 `MinHeight`；
- **验证**：窗口 1920×1040 / 1600×900 / 1200×760（MinSize）三档实测，"暂停检测"按钮
  高度恒为 32 DIP、宽度随窗口不变，布局不挤压变形；设计稿参数对照（导航 30/Padding 0 14、
  按钮 32 与 26、chip 22）。

### 九、胶囊"太尖锐"根因修复（WPF CornerRadius 与 CSS border-radius 语义不同）

- 用户反馈胶囊"太尖锐、和设计图弧度明显不一样"。把设计稿用 Edge 渲染成图、与实机截图
  **同比例放大 6× 并排比对**后定位到根因——不是圆角写小，而是**写大反而变椭圆**：
  · 设计稿 `.btn{border-radius:999px}`：浏览器按"相邻圆角半径之和不得超过边长"的规则
    把所有半径**统一按比例缩小**，最终半径 = 高度/2，端头是**正圆半圆**，顶边/底边
    各留一段水平直边。实测渲染图顶部行宽 / 最大行宽 = **0.675**；
  · WPF `Border.CornerRadius="999"`：按两个方向**分别钳制**（水平→宽/2、垂直→高/2），
    画出来是**椭圆角**——宽按钮两端被拉成长椭圆、收弧太急，视觉上就是"尖锐/橄榄球"。
    同一按钮实测顶部行宽比只有 **0.226**；
- **修复**：新增 `Converters.PillRadiusConverter`（`ActualHeight` → `CornerRadius = 高度/2`），
  资源键 `PillRadius` 注册在 `Styles/AiStyles.xaml`（MainWindow 内再就地注册一份，
  避免 `Window.Resources` 的 StaticResource 解析早于合并字典）。全部药丸模板改为
  `CornerRadius="{Binding ActualHeight, RelativeSource={RelativeSource Self},
  Converter={StaticResource PillRadius}}"`，共 11 处模板绑定：`AiStyles.xaml` 6 处
  （`AiBtn` 主模板 1 处，全部按钮档位 `BasedOn` 共用；`AiChipBase`/`AiChipPlain` 2 处；
  `AiSwitch` 轨道 1 处；`AiSlider` 左右两段轨道 2 处）+ `MainWindow.xaml` 5 处
  （导航胶囊 1、顶栏幽灵钮/关闭钮模板 2、顶栏状态胶囊 2）；
- **为什么不写死半高值**：32 高按钮写 `CornerRadius=16` 在正常字号下正确，但系统字体放大 /
  布局撑高后圆角就不再是半高（实测字号 26 时按钮自动长到 35），药丸退化成圆角矩形。
  绑定 `ActualHeight` 后**高度怎么变圆角都跟着变**，任何 DPI、字体、窗口比例下都是同一个
  正圆药丸——这才满足"基类控件复用不变形"的要求。`CornerRadius` 不参与 Measure/Arrange，
  不会造成布局循环；
- **验证**：同一「暂停检测」按钮在 1920×1040 / 1600×900 / 1200×760 三档窗口实测，
  顶部行宽比 **0.667 / 0.675 / 0.675**，与设计稿渲染值 0.675 一致（修复前 0.226 椭圆）；
  6× 放大并排图弧度完全重合；浅色/深色双主题、监控/配方中心/配方编辑器/检测引擎/
  审计追溯/设备与集成六页目检通过；字号 12→22 时按钮高度恒为 32、字号 26 起自动长高，
  胶囊形状始终成立；
- 顺带修正 `MainWindow.xaml` 头部过期注释：2026-11-22 的 `CornerRadius 7` 短圆角口径
  已作废（当时误判纯药丸"太尖锐"，实际是 WPF 椭圆角造成的），统一改为设计稿半高药丸口径。

### 十、目检脚本 `shot_ui.ps1` 导航坐标修复（audit/settings 截图内容重复）

- **根因**：导航从底部 Dock 收进顶栏并水平居中后，脚本仍用旧窗口内坐标
  （monitor=222 / recipe=352 / engine=481 / audit=619 / settings=749，y=21），实际点到品牌区与
  空白处，`audit.png` 与 `settings.png` 落到同一页面；
- **改为 UIA 动态定位**：按「名称 + `ControlType=RadioButton`」查顶栏胶囊 `BoundingRectangle` 取中心。
  为此导航 `RadioButton` 增加 `AutomationProperties.Name="{Binding Title}"`——原先模板直接绑
  `Icon`/`Title`、控件 `Content` 为空，UIA `Name` 是空串（读屏与自动化工具都取不到名字），
  本项同时修正了导航的无障碍名称；
- **脚本屏幕适配**：进程启动即 `SetProcessDPIAware()`（`GetWindowRect` / `SetCursorPos` / UIA 统一
  物理像素，125%/150% 缩放下不再整体偏移）；启动后 `SW_MAXIMIZE` 统一 1920×1040 目检口径；
  UIA 不可用时回落硬编码基准，并按「导航水平居中 + 当前 DPI」换算（居中偏移不随窗口宽度变）；
- **`-outDir` 参数**：验证时可输出到临时目录，避免覆盖 git 跟踪的 README 文档配图；
- **验证**：全 6 页一次跑通，日志逐页打印 `(uia)` 与真实坐标（682/780/881/982/1089, y=26），
  六张 PNG 哈希两两不同；审计追溯 / 设备与集成 / 配方编辑器目检正确；
- 顺带修正 `MainWindow.xaml` 三窗钮过期注释（关闭钮已是宿主 `AiTopCloseButton`，
  不再用库 `KxTitleCloseButton`）。

## V0.1.11（2026-11-23）配方编辑器 ROI 画布比例对齐监控台 + 全屏标画弹窗 + 帧到达重绘修复

### 一、ROI 画布比例与运行监控台一致（修复 V0.1.10 遗留待办）

- **根因**（两条叠加）：①编辑器画布固定 `Height=240` 铺满整张卡片，形成约 6.7:1 超宽舞台，
  ROI 归一化 0~1000 映射的是整条超宽区，而监控台画面按帧实际像素 4:3 Uniform 内接——
  同一份 ROI 在两处落在画面不同位置；②`RecipeEditorPage` 未订阅 `FrameImage` 变化，
  取像帧到达后舞台比例与 ROI 不重算，坐标口径进一步漂移；
- **新口径**：`RoiViewport` 固定 380 高视口，`KxImageStage` 按**帧实际像素宽高比**
  内接其中并居中（无帧回退 Mock 4:3 = 1280×960；真机换分辨率零改动）——视口放不下自动
  等比缩小、富余自动等比放大，舞台比例恒等于监控台画面比例；ROI 框在编辑器 / 全屏弹窗 /
  监控台三处落在画面同一位置；
- **交互抽取**：`Views\RoiStageController.cs` 承接全部标画交互（拖框移动 / 四角缩放 /
  空白拖画新框 / 数值双向联动 / 订阅帧与 ROI 集合 / 三路联动重绘），页内画布与全屏弹窗
  共用同一份实现，杜绝两套代码漂移；`RecipeEditorPage.xaml.cs` 瘦身为 DataContext glue。
  顺带修好帧到达重绘（控制器订阅 `FrameImage` → 重算舞台尺寸 + 重绘）；
- **布局防溢出**：编辑器右列包 `ScrollViewer`（垂直 Auto / 水平 Disabled），小屏/高缩放时
  整列可滚动，下方数值行不再被挤出窗口。

### 二、全屏标画弹窗（单独开窗，不撑大页面内画布）

- **入口**：ROI 卡标题行新增自绘"全屏标画"图标按钮（四角扩展箭头 Path，方形小钮；
  走 `Theme*` 主题键、`FocusVisualStyle={x:Null}` 清焦点虚线框、hover/pressed 转橙主色，
  与 C 方案胶囊族并排不抢主按钮视觉层级）；MVVM 链路
  `FullscreenRoiCommand → ServiceHub.IHost.ShowRoiFullscreen → AppHost new 窗`；
- **弹窗** `Views\RoiFullscreenDialog.xaml/.cs`：继承库 `ChromeDialogWindow`，默认最大化
  并修正到工作区（`FixMaximizedToWorkArea`，不盖任务栏），保留标题条/系统钮便于退出；
  顶部信息行（实际取像 + 「与监控画面同比例」胶囊 + 工步名 + 操作提示 + 启动取像）、
  大画布、底部选中信息 + X/Y/宽W/高H 数值行 + 退出全屏；
- **同源同步**：弹窗与页内画布共用同一 `RecipeEditorViewModel` 与同一套
  `RoiStageController`——拖框/改数值即时双向同步；`Closed` 里 `Dispose` 控制器
  （摘 VM 订阅 + 舞台事件），防止已关闭窗口被 VM 持续引用；Esc / 标题条 × /
  底部"退出全屏"三种关闭方式（Esc 走基类 `CancelCommand` 口径）。

### 三、主题键

- 新增 `ThemePrimaryFill`（ROI 选中半透明底 + 橡皮筋预览填充）：浅色 `#22F97316`、
  深色同值（深底下自然加深），两套字典同键。

### 四、验证

- MSBuild `AIris.WPF` 0 error（Debug/AnyCPU）；
- 浅色 / 深色双主题实机截图目检：页内画布 4:3 居中、ROI 框位置与监控台同源、
  全屏弹窗大画布比例正确、深色下图标钮/胶囊/数值行全部可读；
- 物理点击回归：全屏按钮开窗 → 拖框移动实测数值同步（X 60→112 回写）→ Esc 关闭 →
  编辑器 ROI 同步刷新；调试拖动未保存，重启后运行数据加载配方文件原值（ROI-A 60,110）。

### 五、文档清理

- 删除 `docs/KxImageStage取像舞台控件方案.md`（设计与键板契约已沉淀在
  `Kaleidoscope.UI\Views\KxImageStage.cs` 头注释，独立方案文档不再维护）；
  README 文档导航/仓库结构注释同步移除该行，调研报告 §10.1 引用改指控件源码头注释。

### 六、调研报告删除 + 落地指南新建 + 设计稿收敛单方案

- **删除 `docs/AIris调研报告.md`**（九章节 + 进度，11404 字），新建 `docs/AIris落地指南.md`
  作为唯一业务/技术事实来源：模型接入四层架构、四协议对照（openai/anthropic/ollama/custom）、
  引擎页配置项与接入验收清单、探活与降级语义、性能预算与已知坑、未完成开发工作清单
  （关键路径 / 硬件接入 / 软件体验收尾，逐项带验收口径）、开发与验收命令、事实来源索引；
- **引用同步**：README（接入段 / 目录树 / 文档导航）、AGENTS.md（唯一事实来源 / 注释规范 /
  铁律 12 / 约定索引 / 冒烟命令）全部改指新指南；主仓 10 个源码文件共 19 处
  「调研报告 §X」历史注释改为落地指南对应章节（`ThirdParty\Kaleidoscope` 两条 §10.2
  注释保留，已在指南开头注明等效契约位置，随下次子模块改动清理）；
- **删除 `docs/branding/`**（A-F 六套 Logo 候选稿 + 预览页）：应用图标已定稿为
  `AIris.WPF\Assets\app.ico`，候选稿不再保留；
- **UI 设计稿重写**（`docs/ui_design_mockups.html`）：从 5 方案 A-E 工作台收敛为落地选定的
  **C 方案「熔金流场」单方案版**，视觉 Token 照抄现有两套主题字典（`ThemeLight/ThemeDark`），
  页面结构对齐 V0.1.11 实况（52px 顶栏导航胶囊 / 4:3 取像画布 / 编辑器画布 + 右栏数值）；
  7 页（6 产品页 + 设计规范）+ 4 弹窗（新建配方向导 / NG 详情 / 审计存档 / 全屏 ROI）可交互，
  明暗一键切换；`[vNext]` 角标标出对现有界面不合理处的重设计，作为后续开发指令：
  ① 监控台检测框三态上色（橙待装 / 绿已装 / 黄虚线等待 / 红 NG）；
  ② 编辑器 ROI 清单与选中数值移入画布右栏（不再压卡片底边）；
  ③ 引擎页「测试连接」就地反馈（在线 ms / 不可达原因，不用翻日志）；
  ④ 设备页左导航项加连接状态点（在线 / 离线 / 未配置）；
  设计规范页给出 Token↔WPF 键对照、控件复用键（先查库再自绘）、状态语义与落地检查清单。
  目检收口：页说明条只在当前页显示；检测标签内收不再压 ROI 标签；引擎页「当前引擎」与日志
  一致（Qwen2.5-VL-7B 在线、Mock 可切换）；Token 逐键对齐两套字典（浅色 Ok/Warn/Ng =
  `#0B7A55/#B45309/#CF4343`、顶栏渐变浅 `#F59E0B→#EC4899` / 深 `#7C2D12→#831843`）；
  新增 URL 直达参数 `?page=&theme=&modal=` 便于单页打印与自动截图。

### 七、README 配图与截图脚本校正

- 「界面速览」的配方编辑器截图按 V0.1.11 新布局重拍（4:3 内接画布居中 + 全屏标画图标钮），
  替换 V0.1.10 的超宽画布旧图；版本徽章 V0.1.9 → V0.1.11；
- `Tools\shot_ui.ps1` 修正：原脚本仍用 V0.1.1 前的底部 dock 坐标，点击全部落空（连拍都是监控台）；
  改顶栏胶囊窗口内坐标 + 窗口原点偏移，并补 `editor`（配方中心 →「编辑」）进入路径；实测
  `editor` 出图正确。

## V0.1.10（2026-11-23）README 全量重写（最新实拍图 + 逐章节对齐 V0.1.9 现状）+ 演示配方 ROI 与 Mock 布局对齐修复

### 一、README 重写（readme skill 流程：事实摸底 → 重拍截图 → 逐章节落稿 → 双脚本核验全 PASS）

- **版本徽章**：V0.1.0-POC → V0.1.9；「检测引擎-留桩待接入」→「可插拔·四协议接入」；
- **界面速览换 6 张最新实拍图**（旧 5 张实为 13:00 拍摄、切页点击坐标失效全成了监控台画面）：
  监控台（全流程 7 步 OK 完成态）/ 配方编辑器（新增，自然语言工步 + ROI 拖拽标画）/ 配方中心 /
  检测引擎（三卡 + 四协议参数 + 服务状态）/ 审计追溯（13 条命中 + 截图规则快照）/ 设备与集成；
- **特性段对齐 V0.1.7~V0.1.9**：补 ROI 拖拽标画（拖框/手柄/拖画）、推理协议层沉库
  `Kaleidoscope.Infer` 四协议、未接入回落可见化（顶栏胶囊/信息行 →Mock/审计 EngineName 如实记录）；
- **「大模型接入（留桩说明）」整节重写为「大模型接入」**：留桩已不存在——HttpDetectionEngine
  已是 InferClient 薄适配；改为四协议部署形态表（自研 /detect、OpenAI 兼容、Anthropic、Ollama
  各带探活端点）+ grounding prompt 与 0~1000 归一化框口径 + 回落语义；
- **文档导航**补 `docs/KxImageStage取像舞台控件方案.md`；**配置段**按 MainSetting.ini 实键核对
  （[Theme] IsDark / [Engine] 九键），删除不存在的笼统描述；
- 核验：`img_inventory.py` RESULT OK（无死引用）+ `check_readme.py` NG=0 WARN=0。

### 二、顺手修复：演示配方 ROI 与 MockScene 布局错位（Mock 全流程此前无法 7 步全 OK）

- **根因**：运行数据配方「电动车控制器 V2.1」的 ROI 在 V0.1.7 调试拖拽后存了盘（ROI-B 被拖到
  (650,260)），与 MockScene 硬编码布局（散热器 (480,280)-(660,500)）错位 → 工步 2 IoU≈0.02 永远
  NG；且工步 3（4 颗 60×60 小螺丝）与工步 2（180×220 散热器）共用 ROI-B 时，IoU≥0.35 对两者
  **数学上不可能同时成立**（小零件对凸包 ROI 的 IoU 上限 ≈0.13）——演示配方从未真正配套；
- **修复**（运行数据 JSON，按 Description「与 Mock 演示布局对齐」的应然状态）：
  ROI-B 回归贴散热器 (470,270,200,240)（IoU≈0.83）；新增 ROI-E 贴 4 颗螺丝凸包 (690,300,170,160)、
  工步 3 RoiId 改指 ROI-E 且 IoU 阈值 0.35→0.10（4 颗全过，matched=4）；ROI-D 贴端子排凸包
  (760,600,170,200)（每颗 IoU=0.40）；ROI-A/ROI-C 本就配套不动；
- **实测**：一键摆齐 → 重检本步 → 工步 1~7 全部 OK 自动推进，「整检通过 · 全流程完成」（OK 7），
  审计落档 8 OK / 5 NG（NG 为修复前真实判定记录，追溯链完整）；
- **遗留待办**（本轮不修）：编辑器 ROI 标画区取像帧（4:3 letterbox 竖条居中）与 ROI 框
  （映射整个超宽舞台）坐标口径错位——V0.1.7 曾验证「垫板出现在 ROI-A 框内」，现状不对齐，
  待查 `Views\FrameRect.cs` 在编辑器舞台（约 6.7:1 超宽）的映射基准。

## V0.1.9（2026-11-22）输入族字色修正：配置框不再像灰色占位文字 + 解码模式下拉补选项

### 一、输入框文字不醒目（用户目检反馈）

- **根因**：`FreshInput/FreshCombo/FreshPwdInput/FreshComboItem` 的前景色统一取库契约键
  `Brush.Fresh.FgMid`，宿主却在两套主题里把它配成 muted 灰（浅色 `#9A8F86`、深色 `#A08FA8`）——
  等于把"用户填写的值/选中的值"当次要说明文字渲染，检测引擎页的协议、解码模式、
  批处理大小读数发灰，和空占位几乎分不出来；
- **修正**：浅色 `#3A3230`、深色 `#EDE6EF`（与两套主题正文同强度）。设计稿 `.tin/.sel`
  的字色本就是主字色，muted 只该留给占位符。改的是同一个契约键，所以全站输入框/下拉/
  密码框/下拉项一次性对齐（引擎/配方编辑/设置/审计筛选/新建配方弹窗）；
- **为什么不逐页改 XAML**：库模板内部用 `DynamicResource Brush.Fresh.FgMid` 写死语义键，
  页级 `Foreground` 改不动下拉的选中值/箭头；键板值才是唯一正解，且两套主题同步换值。

### 二、目检顺带修正：解码模式下拉空白

- `DecodeMode` 绑了 `SelectedItem` 但 ComboBox 没有 `ItemsSource`（红线条目：SelectedItem
  绑字符串时 Items 必须是字符串集合）——框内永远空白、点开无选项。补
  `EngineViewModel.DecodeModeChoices` 三档（hybrid（MTP 优先）/ fast（MTP）/ slow（AR）），
  与 `EngineConfig.DecodeMode` 的 id 映射一一对应；默认值正常回显。

### 三、验证

- MSBuild `AIris.WPF` 0 error；浅色/深色两套主题实机截图目检（`Tools/Shots/v019_engine_light2.png`、
  `v019_engine_dark2.png`）：协议值、`hybrid（MTP 优先）`、批处理 `8` 均为正文强度，
  深色下为亮白；解码模式正常显示且可展开。其它页面输入族共用同一键，同步受益。

## V0.1.8（2026-11-22）引擎切换语义修复：回落可见化 + 状态不再自相矛盾 + 切换生命周期保护

### 一、用户问题：切了引擎 Mock 还在跑、Mock 卡还亮"当前引擎"（真 Bug，非降级设计本身）

- **双"当前引擎"根因**：`EngineViewModel.IsActive` 对 Mock 的判定含 `|| !cur.IsLoaded`——
  选中 HTTP 引擎但探活失败时，Mock 卡与真实引擎卡同时亮橙边+"当前引擎"胶囊，
  用户以为切换没生效。改为严格按当前引擎实例判 Active；回落态单独用
  `Fallback` 属性表达（Mock 卡"回落执行中"，Warn 色胶囊/描边，按钮"切回此引擎"）；
- **降级不可见根因**：`RunMonitor.DetectLoop` 算了 `engineName = _engine.Name + "→Mock"`
  却从未使用（死代码），画面信息行仍写"LocateAnything-3B 0 ms"、审计 `EngineName`
  仍写选中的引擎。改为 `_lastExecName/_lastExecMs` 记录本帧实际执行者：
  信息行显示 `LocateAnything-3B→Mock 0 ms`，判定日志与审计 `EngineName` 同步写
  `X→Mock`（追溯链能看出这单是不是演示替身出的框）；引擎页"单帧耗时"取实际执行耗时；
- **顶栏状态胶囊动态化**：原写死"引擎 在线"，与未接入引擎的事实矛盾；改绑
  `MainViewModel.EngineIndicator`（引擎 在线 / 引擎 回落 Mock / 引擎 未接入），
  订阅 `RunMonitor.StateChanged` 刷新（检测线程事件经 Dispatcher 回 UI 线程）；
- **文案修正**：引擎卡"切换需重载引擎（Mock 演示不中断）"→"（未接入时回落 Mock 演示）"；
  确认弹窗补充"未接入时检测自动回落 Mock 演示（流程不中断，画面信息行会标注 →Mock）"。

### 二、切换引擎的生命周期修复

- **重复探活**：`EngineViewModel.SwitchTo` 先 `engine.Load(cfg)`、`SwapEngine` 内又
  `Load(cfg)`——HTTP 引擎白等两轮同步探活（各 ≤2s）。改为 `SwapEngine` 唯一装载点；
- **旧引擎 Dispose 竞态**：检测线程可能正卡在旧引擎的 HTTP 推理请求里（最长
  `HttpTimeoutMs`），原实现直接 `old.Dispose()` 会撞"请求在飞、client 已释放"。
  新增 `_engineSync + _inUseEngine + _pendingDispose`：切换时若旧引擎在途 → 延迟到
  检测线程本轮收尾（`ReleaseEngineAfterUse`）释放，不阻塞 UI；无在途则立即释放；
  `Dispose()` 收口清理 pending，切回同一实例的场景有防御。

### 三、降级日志节流

- HTTP 引擎持续不可达时，原每帧（300ms）记一条"检测降级"把运行日志刷满；
  `HttpDetectionEngine.Degraded` 加同因 5s 节流（原因变化立即记，同因超 5s 再记一条心跳）。

### 四、验证（构建 + 真机实点 + 截图目检）

- MSBuild 增量构建 0 error；`Tools\click_ui.ps1` 物理点击实点：未接入回落态 →
  切回 Mock → 再切回 LocateAnything，切换链路全通，卡片/顶栏/页头/服务状态一致，无双高亮；
- 明暗两套主题截图目检通过（Warn/Primary/Ok 主题键，无写死色值）；
- 审计 JSONL 实测 `"EngineName":"LocateAnything-3B→Mock"`（切换前记录为
  `"Mock 演示引擎"`），监控台信息行实测 `LocateAnything-3B→Mock 0 ms`；
- 日志节流实测：同一降级原因约 5s 一条，启动链/判定链可读。

## V0.1.7（2026-11-22）ROI 拖拽标画 + 引擎通用协议层（沉库 Infer 五协议）+ JEV 语义决策留口

### 一、配方编辑器 ROI 标画升级：在实际取像上拖拽标定（用户定版需求）

- **取像控件统一（用户指出红框区应对齐主界面取像控件）**：编辑页 ROI 预览从
  纯 Canvas（无帧底抽象框）升级为库 `KxImageStage`（与监控台同控件）+ "启动取像"
  开关——`RunMonitor.StartPreview/StopPreview` 纯预览口（只开相机不出判定，
  相机帧 OnFramePreview 直通 FrameReady；检测运行中调用为无害空操作防双发；
  Start 自动退预览态）；编辑页 PreviewOn/FrameImage（Dispatcher 收口转
  BitmapSource，口径同 MonitorViewModel.OnFrame）；
- **鼠标拖拽三件**（RoiStage.Overlay 上，命中优先级 手柄>已有框>空白）：
  ①拖框移动（起点坐标快照 + Δpx→Δ归一化实时回写）②四角手柄缩放（拖哪角哪角动、
  对角锚定、过角翻转）③空白拖画新框（橡皮筋预览，松手 ≥15×15 归一化落框
  AddRoiAt 自动编号选中，过小防误触放弃）；
- **数值双向联动修复**（旧版数值框改 X/Y/W/H 画布不刷新）：RoiVm INPC 三路
  订阅（选中 ROI 属性 + SelectedRoi 引用 + 集合变化）统一走 RedrawRois；
- **坐标换算抽 FrameRect 共用**（Views\FrameRect.cs）：按帧实际像素宽高求
  Uniform letterbox 内接矩形——监控台旧 4:3 硬编码同步替换（真机换分辨率零改动）；
- **关联缺口一次改干净**（铁律 8）："判定区域"下拉从硬编码 ROI-A~D 改绑
  Rois 集合同源（ChoiceItem，全画面空选项保留）；左侧 ROI 清单选中高亮修复
  （旧 DataTrigger 绑 ContentPresenter.IsSelected 属性不存在恒不触发，改绑
  VM IsSelected + SelectedRoi setter 维护）；
- **沉库 V2.3.7**：KxImageStage 内置 Overlay 构造 `Collapsed+IsHitTestVisible=false`
  且从未翻开（V2.3.6 遗留缺陷——监控台检测框此前实际也没显示出来）改恒可见+可命中，
  `_idle` 空态文本补 IsHitTestVisible=false（居中文本区不吞点击）。

### 二、引擎页功能补完 + 通用推理协议层（沉库 Kaleidoscope.Infer V2.4.0）

- **真 Bug 修复：弹窗返回值断裂**——`ConfirmDialog`/`NewRecipeDialog` 点确认只设
  自定义 `Result` 属性，`ShowDialog() == true` 恒 false：**"切换引擎没反应"的根因**
  （确认弹窗后静默 return），删除确认/新建配方返回值同样隐性失效；改设
  `DialogResult = true`。实测：切换 Mock→Qwen→LocateAnything→Mock 四次全通；
- **沉库 `Kaleidoscope.Infer`**（主库 Infer\ 目录，命名空间 Kaleidoscope.Infer，
  零第三方依赖：JSON 手拼/轻扫 + BCL System.Net.Http + TLS1.2 静态收口，图像
  JPEG byte[] 过接口不引 System.Drawing）：
  - `InferEndpoint`（协议/地址/模型/Key/超时）+ `IInferTransport`/`InferTransportBase`
    （POST/GET 收口，失败转 Ok=false 不抛异常——检测线程 GetResult 口径）；
  - 四视觉协议 transport（信封差异收口，语义统一"图+提示→文本"）：
    **OpenAi**（/v1/chat/completions，image_url=data URI，Bearer）、
    **Anthropic**（/v1/messages，base64 source 块 + x-api-key/anthropic-version 头 +
    system 顶层字段，content[].text 拼接）、**Ollama**（/api/chat stream:false，
    images=**裸 base64 数组**非 data URL）、**CustomJson**（POST /detect 自研契约
    {model,prompts,image_b64}→{ok,detections[]}，调研报告 §10.2 既定，框直出）；
  - `BoxParser`：回包文本 → 0~1000 归一化框。策略①JSON bbox_2d 数组（Qwen2.5-VL
    grounding 默认形态，容 ```json 围栏）②\<box\> 标签（LocateAnything 文本形态两种
    写法）；钳位/端点校正/零面积丢弃；**无置信度字段（语义红线）**；
  - `InferClient` 门面：DetectAsync（三句式 grounding prompt：输出 JSON bbox_2d、
    坐标 0~1000 归一化、只输出 JSON）+ ProbeAsync 探活（openai:/v1/models、
    ollama:/api/tags、custom:/health、anthropic:配置即就绪）；
- **HttpDetectionEngine 桩→薄适配**：持有 InferClient；Load=同步探活（失败不抛
  显示降级态，口径不变）；EngineConfig 加 Protocol/ApiKey（INI [Engine] 落盘；
  引擎卡默认协议注入 locateanything=custom、qwen-vl=openai）；EngineRegistry
  Create 带 cfg 协议初值注入；
- **引擎页 UI**：推理协议下拉（四协议注解部署形态）+ API Key 框 + "测试连接"
  按钮（表单当前值 ProbeAsync 报结果/耗时，空地址友好报错——实测通）+
  Apply 改"落盘+HTTP 引擎热重载重探活"（旧版只落盘不生效）+ 解码模式/批处理
  仅 custom 协议可编辑（其余灰显+提示属服务端参数）。

### 三、JEV 语义决策留口（调研结论：不进生产、不碰判定链）

- **调研结论**：JEV 是 TypeSafe 云端文本决策模型（Decisions API：Choice/Score/Noul
  三题型 + 校准概率）——**无图像输入**（官方"Images not supported (yet)"）、闭源
  无本地权重，与 AIris 全本地离线红线错位；"语义识别"主战场（图→零件框）是
  LocateAnything/Qwen2.5-VL 的职责，JEV 进不去；判定链永远走确定性几何规则。
  **结论：不做生产接入，留三层低成本口子**：
  - 沉库第五协议 `DecisionsTransport`（兼容 TypeSafe /v1/systemone 与 OpenRouter
    /api/alpha/decisions 端点，choice/noul 组包解包）；
  - Core 扩展点 `ISemanticDecisionService`（Choice/Noul 最小模型）+ 默认
    `NullSemanticDecisionService`（恒"未启用"零开销不联网）；RunMonitor 构造可注入；
  - 唯一应用点：审计 NG 原因归纳增强（落 NG 档时问一道 Choice：漏装/错装/位置
    偏移/遮挡/其他 → AuditRecord 新增 `SemanticNote` 可选字段；失败静默不阻断；
    INI `SemanticDecisionEnabled=OFF` 默认关——实测默认关=行为与现状一致）。

### 验证

- MSBuild 整编 0 error（Kaleidoscope/Core/WPF 四工程级联）；
- `Tools\probe_ng_semantic.ps1` 全绿（NG-SEMANTIC-OK）；
- 实点回归（click_ui.ps1 + agent-desktop）：编辑页启动取像→垫板出现在 ROI-A 框内
  （取像与 ROI 同源对齐）→ 点框选中（橙描边+四角手柄+清单高亮+数值跟随 X/Y/W/H）
  → 拖动移框（X 60→735 实测生效）→ 空白拖画新框（落框自动选中）→ 引擎页切换
  弹窗→真切换（卡高亮/副题/协议回填/灰显联动）→ 测试连接（空地址/真实地址两态
  友好降级）→ 明暗主题键全走 Theme* 无写死色。

## V0.1.6（2026-11-22）🧠 引擎图标 + 网格 64px + 空场扫描动效恢复（用户三项定版）

- **导航/页头图标 `⚙`→`🧠`（三处同语义层同步）**：顶栏导航胶囊（EngineViewModel.Icon，
  面包屑同源）+ 检测引擎页页头 + 头注释图示——大脑 = AI 推理引擎语义，与"设备与
  集成"的 ⚙ 齿轮不再撞图标；设置页 ⚙ 保留（设备语义正确）；
- **取像舞台网格步距 18→64px（用户反馈：网格太小）**：MonitorPage 一行
  `GridStep="64"`（控件既有 DP，无需改库）——步距对齐之前 Mock 台面网格口径；
  像素验证：y=520 行 16 条网格线相邻间距逐条 64px 精确命中；
- **空场回无帧空态（用户反馈：没帧时看不到扫描动效）**：根因 = Mock 相机 8fps
  持续出帧，空场透明帧按控件契约（FrameSource 非 null=有帧）停了扫描。修复 =
  `CameraFrame`/`FrameEventArgs` 加 `IsEmptyScene` 标记（真机恒 false，此场景
  天然不存在）→ Mock 空场判定 `IsSceneEmpty()`（真源 = MockScene 单例在场计数
  CountOf；PartLayout 是静态布局表不能当在场判断）→ MonitorViewModel 空场帧
  FrameImage=null 回空态。**双向连拍实锤**：空场 diff=93693（"等待画面…"+扫描
  光带在跑）/ 摆零件后 diff=0（扫描停）——画面上没东西=空态有扫描，有画面=停。

## V0.1.5（2026-11-22）取像显示傻瓜化：全舞台一层网格+扫描（沉库 V2.3.6 帧显示管线 + 扫描联动）

### 全舞台一层网格+扫描（用户反馈：红框抓到画面矩形内双层网格叠加）

- **根因**：Mock 合成帧自画台面装饰（暖白渐变底 + 64px 网格 + 画面内水印）——
  与 KxImageStage 舞台网格两层叠加；真实监控视频不带装饰网格，画面内容层不该有；
- **Mock 帧透明化**（MockCameraSource.Compose）：去台面渐变底/64px 网格/水印，
  只画零件（透明底 32bppArgb；文字抗锯齿 ClearType→AntiAliasGridFit 防透明底
  脏边）——舞台"一层网格+扫描"透底，letterbox 与空态露网格、零件浮在舞台上；
- **Imaging.ToBitmapSource 保 alpha 重写**：弃 GetHbitmap 路线（HBitmap 无 alpha
  语义，透明像素压成黑底）改 LockBits 直拷；**alpha 语义红线（实测教训）**：GDI
  Format32bppArgb=直通 alpha，目标格式必须 `PixelFormats.Bgra32`——喂 Pbgra32
  （预乘）会把半透明像素当预乘值解读，显示叠底溢出爆白（透明帧零件区全糊白）；
  顺带 8fps 高频路径免 GDI 句柄开销；
- **四参数对标设计图核对（不改值）**：舞台底/网格线/扫描色/网格步距（GridStep）
  均为控件 DP 可调，当前宿主键板值与设计稿 `.cam-img` 逐项吻合（浅 #E9EEF8 /
  rgba(80,100,160,.10) 18px / 深 #060B16 / rgba(126,150,190,.10) / 扫描 C 方案
  主色 10%），无需调整。

### 取像控件傻瓜式升级（用户要求：对标 HJVision 封装，外部调用零黑盒负担）

- **库 KxImageStage 内置帧显示管线（Kaleidoscope V2.3.6）**：帧图/空态文本/
  叠加画布 `Overlay`（画面上扫描带下）三件内置——宿主一行绑定即得完整取像显示，
  单图/实时视频通吃（帧源与显示解耦，对标 HJVision VisionPreviewHost 傻瓜式口径）；
- **扫描联动（用户要求，封装内建）**：显示图像/实时视频自动停扫描动效、清帧回
  空态自动恢复——只有初始空态（无帧）才有扫描，外部调用自动保证宿主零接线；
- **MonitorPage 傻瓜式迁移**：手工 Image/空态文本/OverlayCanvas 三件删除，一行
  `FrameSource="{Binding FrameImage}" IdleText="等待画面…"`；叠加框 code-behind
  改取 `Stage.Overlay`（Stage 别名=StageGrid，坐标换算口径零改动）；
- 库侧同轮：双喂帧入口（绑定式 FrameSource / 命令式 ShowFrame/ClearFrame 任意
  线程安全，二选一红线）、IdleText/FrameStretch DP、纯装饰用法零破坏、README
  复用索引与方案文档 3.7 节同步（含验证：浅色屏摄逐位 #E9EEF8 命中/网格透底/
  透明帧零件浮层清晰/有帧连拍 diff=0 扫描停实锤）。

## V0.1.4（2026-11-22）取像舞台控件落地：扫描动效 + 深色舞台（沉 Kaleidoscope 库 V2.3.5，三色可配置）

### 胶囊标签形状统一（用户反馈：页面标签"太尖锐"，同 V0.1.1 顶栏问题）

- **全工程胶囊标签整改（8 处）**：监控台判定胶囊/引擎页引擎状态+卡片状态+许可证/
  配方中心计数+启用中/已停用等手写 `CornerRadius=99/7` Border 全部换成库
  **`KxChip`/`KxChipText`**（Kaleidoscope V2.3.5 新件，CornerRadius=7 圆角矩形档，
  与 StatusChip 药丸档并存、同 Tag 语义）——手写胶囊清零，形状归库口径禁再漂移；
  带状态配色的胶囊改 `BasedOn="{StaticResource KxChip}"` 派生（形状库值、配色宿主）；
  许可证标签直接用 Tag="Warn" 语义档（删手写 WarnDim/Warn 配色对）；

### 取像区舞台化（用户诉求：设计稿扫描动效 + 深色模式舞台观感 + 沉库复用）

- **取像区换库控件 `ui:KxImageStage`**（Kaleidoscope.UI V2.3.5 新件，AIris 反哺）：
  MonitorPage.xaml 取像区 StageGrid（原裸 Grid）替换为取像舞台控件——舞台底 +
  18px 网格底纹 + 主色扫描光带动效（4.2s linear 上→下循环，设计稿 sScan 口径）
  三件装饰内置；控件继承 Grid，三子元素（StageImage/OverlayCanvas/空态）原样保留，
  **MonitorPage.xaml.cs 零改动**（ActualWidth/Height 与 SizeChanged 坐标换算口径不变）；
- **三色可配置（用户要求）**：控件主体颜色（舞台底/网格线）与扫描颜色均为可配置
  DP（`StageBackground`/`GridLineBrush`/`ScanBrush`，默认 null=主题键自动配色、设
  本地值=项目自定义原色）——宿主键板三键即 C 方案口径：深色舞台底 #060B16、
  主蓝灰网格 10%、扫描光带主色 #F97316 渐变（0.46 停靠最亮/92% 渐隐，两板同值）；
- **深色模式修正（本轮痛点）**：此前取像区无背景、深色下透出 ThemePanelBg 紫面板色；
  宿主两板同加 `Brush.Shell.Stage*` 三键后，深色 = 设计稿的深色"相机舞台"
  （#060B16 底 + 网格），浅色 = #E9EEF8 底 + 网格，深浅切换自动跟随；
- **性能护栏（分层窗软渲染）**：库级总闸 `KxShellDefaults.EnableStageScanMotion`
  （缺省 true，一键关全局）+ 实例级 `ScanEnabled`；IsVisible=false/Unloaded 双停
  （BeginAnimation(null)）防空转——最小化/切页实测 CPU 86%→15% 护栏有效；
  **动画帧率降档 `Timeline.SetDesiredFrameRate(anim, 10)`**：验证期实测 60fps 动画
  时钟稳态 ~85-90% 单核（分层窗每 tick 整窗重绘），降 10fps 后稳态 ~50%（动画
  纯增量 ~10%，Mock 合帧/帧流为 V0.1.0 固有 ~40%）——慢位移扫描带肉眼平滑无跳感，
  **通用口径沉库：分层窗软渲染宿主装饰性动画必须设 DesiredFrameRate ≤10fps 量级**；
- 方案文档沉淀 `docs/KxImageStage取像舞台控件方案.md`（摸底事实/层序设计/键板契约/
  实施清单/验证清单，含实施后性能实测修正节）；调研报告追加第十章（开发进度与
  剩余工作六项）；
- 库侧同轮：缺省键板两板各补 3 键（两板键集一致）、README 复用索引场景表登记
  （含适用边界：纯 WPF 适用，HWND airspace 场景不适用）、库 CHANGELOG V2.3.5。

## V0.1.3（2026-11-22）壳圆角体系 + 运行日志系统 + 顶栏 hover 白垫与胶囊居中

### 窗体圆角（用户反馈：窗口化要圆角、全屏不要）

- **主窗窗口化真圆角落地**：MainWindow 根内容包根 Border（`RootShell`，
  CornerRadius=16，C 方案 radius 口径与弹窗卡片 `Fn.ShellRadius` 同值）——
  基类 `ChromeBorderlessWindow` 的分层真圆角三件套（AllowsTransparency + 透明窗底 +
  根 Border 圆角/Clip 联动）此前因宿主根元素是 Grid 而一直处于"纯壳降级"（圆角失效）；
  **根因两处**：①根元素非 Border → 基类 `Content as Border` 解析失败；②Window 级
  `Background="{DynamicResource ThemeWindowBg}"` 本地值覆盖基类 Transparent →
  四角实底直角；修复=删 Window Background 本地值 + 根 Border 设 ThemeWindowBg 兜底；
- **最大化归零 / 还原恢复**：基类 StateChanged 联动（ApplyMaximizedCorner +
  UpdateRootClip）原样生效；XAML 初始 `WindowState="Maximized"` 的 StateChanged
  早于根 Border 解析、基类联动跳过 → MainWindow code-behind Loaded 补一次归零
  （时序补偿，窗口化启动不走本支）；
- **验证**：PrintWindow 四角 alpha + 角部放大目检——窗口化四角圆弧干净、
  最大化四角直角贴满工作区、再最大化归零复现 ✓；

### 弹窗圆角收边（用户反馈：NG 详情弹窗圆角没处理干净）

- **根因=投影被窗矩形裁出四角脏晕**：屏摄（CopyFromScreen）实测弹窗四角圆弧外
  一圈灰色阴影月牙——`KxShellDefaults.ShadowMargin=0`（抄 HJVision 口径）时弹窗
  卡片贴满窗矩形，`Fx.WinShadow`（BlurRadius 24）画在卡片四周的投影被窗裁掉，
  只剩圆弧外月牙区的残余灰晕，白底主窗上看即"圆角脏边"；
- **修复=ShadowMargin 0→14（库 CureLine 缺省口径）**：弹窗骨架根 Grid 恢复
  投影留白，浮层投影完整绘制（四周柔和渐隐），四角弧外纯透明不再有脏晕；
  全部弹窗（NG 详情/新建配方/确认/存档）同基类自动生效；屏摄复验：NG 详情与
  新建配方弹窗四角弧线干净 + 投影自然 ✓；

### 顶栏钮 hover 白垫（用户反馈：🌓/最小化/最大化悬停要对齐导航胶囊效果）

- MainWindow.Resources 新增 `AiTopGhostButton` 顶栏幽灵钮样式：hover 白 20%
  （#33FFFFFF）CornerRadius=7 圆角垫 + 白字（形状同导航胶囊口径），应用于
  🌓 主题切换、最小化 —、最大化 ▢ 三钮；关闭钮保持库 `KxTitleCloseButton`
  红底 hover 不动（警示语义）；物理鼠标实点 hover + PrintWindow 放大目检三钮
  白垫全部生效 ✓；

### README / 文档

- **README 架构图补 ThirdParty/Kaleidoscope 关系**：新增 subgraph
  `KAL["ThirdParty/Kaleidoscope（git submodule）"]`（Kaleidoscope.UI 窗壳/样式 +
  Kaleidoscope 相机适配器/存储/PLC 通讯两卡），主窗 UI 虚线指向壳复用、Core 虚线
  指向真机接入选件；架构说明补"壳与控件复用"条目；语法沿用 id["label"] 引号化
  红线（V0.1.2 教训），静态核对 12 label 全引号化 ✓；
- Tools 新增 `shot_corner_check.ps1`（圆角/hover/弹窗四角回归截图脚本，
  PrintWindow 四角 alpha 检查 + 屏摄角部放大）；

### 日志系统 + 监控台右栏改造（2026-11-22 用户反馈：空白区加日志、状态行收拢、胶囊居中）

- **软件日志系统升级为"文件 + UI 实时流"双出口**：AppLog 新增 LogLine 数据模型 +
  环形缓冲（300 条）+ LineAdded 事件（任意线程触发，UI 订阅方 Dispatcher 收口）+
  Snapshot 首屏回放；落盘链路不变（按天滚动日志），库日志出口（LogSinkInjector）
  自动汇入双出口；
- **Core 层关键节点补日志**（RunMonitor）：最终判定结论（工步/OK-NG/引擎/存档号）、
  配方装载、启动/暂停、引擎切换、整检完成——此前只有相机/引擎/异常进日志，
  判定结论与运行状态不进；
- **运行监控台右栏底部改"运行日志"面板**：原状态行收拢进面板头部（标题 + 状态省略
  截断 + OK/NG 计数同行，不再单独占一行），日志 ListBox 占右栏剩余全部空间
  （右栏 StackPanel→Grid 四行布局），级别着色（WARN 橙/ERROR 红）、新行自动滚到底；
- **【铁坑】布局重入栈溢出（0xC00000FD 崩进程）**：Logs_CollectionChanged 里同步
  ScrollIntoView + UpdateLayout 会造成 UpdateLayout 重入递归（布局中又触发布局）——
  修复=滚动延后一拍 `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`，
  踢出布局周期再滚；WPF"CollectionChanged 里滚动 ItemsControl"通用红线；
- **顶栏状态胶囊文本四向居中**：固定 Height=26 + Padding 10,0 + 文本
  HorizontalAlignment/VerticalAlignment=Center（原 Padding 10,4 行盒偏移不居中）；
- 验证：日志面板实时滚动（启动链/MockBridge/引擎切换/配方装载/相机启动/运行启停/
  NG 判定全链路可见）+ 状态收拢 + 胶囊居中，PrintWindow 截图目检全过 ✓；

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
