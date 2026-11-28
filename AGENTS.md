# AGENTS.md — AIris AI 智瞳（自然语言驱动的通用视觉检测与防错平台）项目指南

> 本文件是 AI 助手在操作本项目前的**强制前置阅读**。开工前先读本文件，明确角色、约定与红线。
> 优先级：本文档 > 项目已有代码风格 > 通用最佳实践。
>
> **项目是什么**：AIris（中文产品名「AI 智瞳」，英文 repo 名「AIris」= AI + Iris/虹膜）——
> 相机盯着工位，用自然语言+配方告诉 AI 检测什么、怎么判定，输出工步引导与 OK/NG 防错判定，
> 全本地离线部署。装配防错是**首个落地场景**，但产品定位是通用视觉检测平台（安全监控、
> 缺料提醒、SOP 合规等场景复用同一套引擎）。Slogan：「一句话，教会 AI 盯现场」。
>
> **唯一事实来源**：业务/技术决策看 `docs/AIris落地指南.md`（模型接入四协议/许可证/性能预算/
> 未完成开发清单）；UI 视觉与交互看 `docs/ui_design_mockups.html` 的 **C 方案「熔金流场」单方案版**
> （橙粉渐变/胶囊按钮/大圆角/顶栏导航胶囊/浅色默认）——写界面时对照设计稿，观感对位不是参数对位。
>
> **大模型现状（重要）**：检测引擎（LocateAnything-3B / Qwen2.5-VL-7B）**尚未接入**，
> `AIris.Core\Detection\IDetectionEngine` 是唯一接入面——真实接入=实现新引擎类或填实
> `HttpDetectionEngine` 的服务调用，**禁止绕过接口在页面/VM 里直接调模型**。当前全流程
> 演示走 `MockDetectionEngine` + `MockCameraSource` + `MockScene` 三件套联动。

## 项目角色

你是本项目的**资深开发工程师**，负责：①按设计稿 C 方案实现/完善 WPF 界面与功能；
②Core 层业务逻辑（配方/工步/判定/审计）；③为大模型与真机相机接入预留的接口保持干净。
改动必须**可编译、可运行、风格统一**，关键改动后更新 `CHANGELOG.md`。

## 工程结构

| 工程 | 作用 | 注意 |
| --- | --- | --- |
| `AIris.Core\` | 纯逻辑层（net472 类库）：模型/检测引擎/相机/判定/工步/存储 | **禁止引入任何 UI 依赖**（WPF/WinForms 都不许）；SDK 风格 csproj |
| `AIris.WPF\` | WPF 主工程（MVVM：Views/ViewModels/Services/Styles） | 输出到 `00_ExeBuild\`（唯一运行目录）；应用图标 `Assets\app.ico` |
| `ThirdParty\Kaleidoscope\` | git submodule（自研通讯库 + WPF 控件库） | **submodule 提交顺序红线**：改库代码先在 submodule 内 commit/push 到其 GitHub，回主仓 `git add ThirdParty/Kaleidoscope` 记录新指针，再连同主仓改动提交；主仓先推会把别人 fetch 不到的指针发上去 |

- **Kaleidoscope 复用面**：`Kaleidoscope.UI`（ChromeDialogWindow 弹窗基类 / ChromeBorderlessWindow
  主窗骨架 / FormField 表单行 / Kx* 样式族 / DefaultLight·Dark 契约键板）；`Kaleidoscope` 主库
  （`Cameras/` KxCamFrame 中性帧+海康/大恒适配器——真机相机接入时用；`Stores/KxSqliteStore`
  审计存储将来可换；`MockBridge`/`LogHelper.LogAction` 注入点）。
- **多目标裁剪**：根 `Directory.Build.props` 设 `KaleidoscopeUITargetFrameworks=net472`
  （AIris 是 net472 宿主，库只还原 net472 产物，离线机不需要 net8 targeting pack）。
- **MVVM 分层**：VM 不 new Window（开窗走 `IAppHost`/`IDialogService`）；View 只留 glue
  （DataContext/RequestClose/Loaded 三行），其余全绑 VM；弹窗 XAML 根直接继承
  `ui:ChromeDialogWindow`；主窗继承 `ChromeBorderlessWindow` 自绘 C 方案视觉。
- C# 语言版本 **7.3**（net472 默认，勿用 C# 8+ 语法：无 using 声明/switch 表达式/??=）。

## 技术栈

- **.NET Framework 4.7.2 + WPF**（SDK 风格 csproj，通配包含文件）；**必须 64 位进程**
  （AnyCPU + Prefer32Bit=false，Kaleidoscope 相机 SDK wrapper 是 64 位原生）
- Newtonsoft.Json（NuGet，配方/审计 JSON 序列化）；配置 INI（自研精简 IniFile，UTF-8 字节级读写）
- 主题：两套 ResourceDictionary（`Styles/ThemeLight.xaml`/`ThemeDark.xaml`）热切换，
  窗口只用 `Theme*` DynamicResource 键，**不许写死色值**；库契约键段（Brush.Shell.* 等）
  **必须写字面值，禁止 StaticResource 元素转发**（net472 BAML 键值错位实测炸主窗）
- 库样式接入：App.xaml MergedDictionaries 前位合并
  ChromeStyles/FreshStyles/ShellStyles/DefaultLight（或宿主键板补契约键段）；启动最顶
  `KxShellDefaults` 十一行（任何窗创建之前设）
- UI 共享控件（弹窗基类/标题栏/表单行/状态胶囊/按钮/输入框）一律复用 Kaleidoscope.UI 的
  Kx*/Fresh* 族，缺样式先在宿主 AppStyles.xaml 用 `BasedOn="{StaticResource 库键}"`
  再造，禁止手抄样板；C 方案色值只进两套主题字典

## 仓库与提交

- 远程 `origin`（GitHub），主分支以 `git branch --show-current` 实际为准；
  **任何推送/领先判断先确认当前分支与 upstream**，参照系只能是 `git log @{u}..HEAD`。
- **版本号递增纪律（2026-11-22 用户定版，勿再提醒）**：用户没有明说的情况下，
  **只递增第三位小版本号**（V0.1.0 → V0.1.1 → V0.1.2 …），禁止擅自升次版本（0.2.0）、
  大版本（1.0.0）或加版本后缀花样；升第二/第一位只在用户当次明确说出版本号时执行。
  配套口径：**已提交推送的版本条目不再追加 `###` 小节**——推送之后的新工作一律开
  新的小版本条目（如 V0.1.1），保持"版本段 ↔ 提交"一一对应可回溯。
- commit message 由全局技能 `git自动提交推送` 从 CHANGELOG 最新版本小节自动生成，
  **禁止 AI 手打 message 正文**；提交用 `--explicit` 显式暂存（逐个 `git add <路径>`），
  **禁止 `git add -A`**。
- `00_ExeBuild\Logs\`、日志、exe/pdb、运行数据不入库。

## HslCommunication 授权（PLC 通讯前提）

- **授权文件部署**：`HslLicense.dat`（许可证密钥清单）+ `hsl.dat`（应用配置数据）必须拷到运行目录
  `00_ExeBuild\`（源头仓 `E:\Project\HslCommunicationLicense`，GitHub `lq700212/HslCommunicationLicense`）。
  Kaleidoscope 的 PLC/仪器通讯走 HslCommunication 商业库，缺授权文件时库侧行为受限。
- **红线**：两文件为程序生成的加密内容，禁手工编辑；不入库不分发不写进文档（.gitignore 已显式排除）；
  更换授权走"整体替换文件"，不做局部修改。

## Mock 演示三件套（无相机无模型跑通全流程的关键）

| 件 | 文件 | 作用 |
| --- | --- | --- |
| MockScene | `AIris.Core\Detection\MockScene.cs` | 单例场景状态：哪些零件在画面上（按零件标签增删）；演示控制入口 |
| MockCameraSource | `AIris.Core\Camera\MockCameraSource.cs` | 按 MockScene 状态 GDI+ 合成俯拍画面（垫板矩形/散热器/螺丝圆点），25fps 线程 |
| MockDetectionEngine | `AIris.Core\Detection\MockDetectionEngine.cs` | 按 MockScene 状态返回归一化框，模拟"即指即检" |

- 运行监控台的演示控制条（放上一个/拿走一个/一键摆齐/重检本步）驱动 MockScene，链条：
  画面合成 → 引擎出框 → JudgeEngine 判定（IoU×数量×稳定帧）→ WorkstepEngine 推进 → 审计落档。
- **唯一 Mock 开关 = 顶栏「Mock 数据」按钮**（2026-11-28 收口）：`ServiceHub.MockMode` ↔
  INI `[App] MockMode`，ON 时相机与引擎**同时**切演示数据、演示控制条才显示；
  OFF 时按设备页/引擎页配置恢复真实设备（真实不可达时按既有语义如实显示回落/离线）。
  **引擎卡列与相机类型下拉都不再有 Mock 选项**，别再把 Mock 加回这两处。
- 真机接入时：相机实现 `ICameraSource`（或包一层 Kaleidoscope 相机适配器）、检测实现
  `IDetectionEngine`，Mock 三件套不删（演示/回归一直要用）。

## 铁律（违反即返工）

1. **文件编码必须是 UTF-8**。禁止用 PowerShell `Add-Content`/`Out-File` 默认编码写含中文的文件
   （会写成 GBK）。写文件用 write 工具；**ps1 一律 UTF-8 with BOM + CRLF**（PS5.1 对无 BOM 脚本按
   ANSI 解码，中文注释直接引发 ParserError）。
2. **终端有 GBK 编码显示假象**：判定文件内容禁用肉眼判码；只用严格解码器或码位级断言
   （`$t.Contains([string][char]0x4FEE)`）；字符级检查禁用 `IndexOf(string)`。
3. **改动后必须构建验证**：WPF 工程构建 0 error 才算完成。
   **SDK 风格工程新增文件有时漏编**（增量构建不发现新文件）——若报"成员不存在"，删
   `AIris.Core\obj`+`bin` 后 `-t:Restore,Build` 整编。
4. **运行配置不当源码改**：`00_ExeBuild\` 是编译输出兼运行目录（`Data\Recipes\`、`Data\Audit\`、
   `MainSetting\MainSetting.ini`、`Logs\` 是运行数据）。配方/审计/INI 都是现场数据，调试时
   改了要记得还原，不进 git。
5. **不主动 commit/push**，除非用户明确要求；提交前 `git status` + `git diff` 确认范围。
6. **代码注释要详细**：关键方法写清"做什么 + 为什么 + 怎么改"；XAML 窗体文件头注释带
   ASCII 布局图；Core 业务类头注释写清与设计稿页面/落地指南章节的对应关系。
7. **任务归类按"动了什么"不按"主业是什么"**：新建/改造任何界面即界面任务，先对照
   设计稿 C 方案逐项对位，交付时截图目检汇报；Core 任务附带改界面同样触发。
8. **改动默认改干净改彻底**：删旧逻辑不留新旧兼容分支/回落/灰度开关，旧键、旧调用点、
   旧样式键**同步一次改到位**；仅当用户当次明确要求兼容某点才例外。
9. **UI 改动必须截图目检**：Edge/PrintWindow 截图对照设计稿 C 方案，发现不对当场修
   （绑定静默失败、浅色下写死深色这类问题只有目检能抓）。
10. **UI 开工先查 Kaleidoscope 控件复用索引，库内确无对应物才允许自绘**（2026-11-22 主窗导航胶囊
    实训固化：漏查库内 `KxLangButton` 自绘 ControlTemplate 还漏清焦点虚线框）：
    - 新建任何控件/样式/转换器前，先跑
      `grep -o 'x:Key="[^"]*"' ThirdParty/Kaleidoscope/Kaleidoscope.UI/Themes/*.xaml`
      对着库 README 的「**控件复用索引（按场景查）**」表查一遍（弹窗壳/三窗钮/导航胶囊/Tab/按钮
      六档/输入/状态胶囊/卡片/KPI/表格/标签/开关/转换器全有现成键）；
    - 库内有对应物 → 直接用样式键/控件类；近似形态 → `BasedOn="{StaticResource 库键}"` 再造；
    - 确无对应物才自绘，且必须：走 `Theme*` 主题键（禁写死色值）+ `FocusVisualStyle="{x:Null}"`
      （WPF 默认焦点虚线框是"胶囊效果很奇怪"的元凶）+ 登记本文件索引表评审是否沉库。
11. **颜色一律 `Theme*` 主题键，禁止写死色值**；TextBox 类控件**禁设非零垂直 Padding**
    （FreshInput/KxTextBox 模板垂直内缩会裁掉文本行——AIris 实训 19 处返工；居中交给
    VerticalContentAlignment）；ComboBox 的 SelectedItem 绑字符串时 Items 必须给字符串集合
    （绑 ComboBoxItem 集合 = SelectedItem 永远对不上显示空）；ListViewItem 自定义模板必须用
    **GridViewRowPresenter**（Columns 绑 `GridView.ColumnCollection` 附加属性）按列渲染单元格——
    误用 ContentPresenter 只显示 Content.ToString() 整行变类名（2026-11-22 AuditPage 实踩）。
12. **检测判定语义红线**：检测框**永不显示置信度**（模型无置信度输出，落地指南 §2.7）；
    位置判定只走 ROI-IoU 几何规则 + 连续 N 帧稳定，几何判定是确定性的、可审计的；
    判定口径改动必须同步落地指南与审计存档字段。
13. **WindowChrome 命中红线**：无边框窗（ChromeBorderlessWindow/ChromeDialogWindow）的
    WindowChrome.CaptionHeight 区域内，未设 `shell:WindowChrome.IsHitTestVisibleInChrome=True`
    的交互元素点击一律被吞成拖窗——"按钮看起来正常却全无响应"的头号根因（2026-11-22 实测：
    底部 dock 在命中区外正常，收拢进 52px 顶栏后导航/主题钮/三窗钮全哑）。顶栏/标题条内
    每个按钮都要逐个放行；宿主 CaptionHeight 应=顶栏全高（空白处拖动+双击最大化）。
    配套：**UI 交互验证必须物理实点**（PrintWindow/静态截图验不出点击吞没）——
    `Tools\click_ui.ps1`（SetCursorPos+mouse_event）+ agent-desktop 截图链路。

## 约定索引

| 动这块 | 先读 |
| --- | --- |
| 新弹窗创建（ChromeDialogWindow 骨架/标题条/隐式样式） | `ThirdParty\Kaleidoscope\Kaleidoscope.UI\README.md` + `Views\LicenseDialog.xaml`（最简样板） |
| **新建控件/样式前查库内现成件**（场景→控件对照表 + 自绘三条件 + 检索命令） | `ThirdParty\Kaleidoscope\Kaleidoscope.UI\README.md`「**控件复用索引（按场景查）**」节——开工先查，禁止重复造轮子 |
| **ROI 标画交互 / 画布比例口径 / 全屏标画弹窗**（序号 10.1 自绘钮登记见下） | `Views\RoiStageController.cs`（三态拖拽 + 按帧比例内接舞台 + 订阅联动）+ `Views\RoiFullscreenDialog.xaml`（最大化弹窗 + 同源 VM） |
| 自绘控件登记（待评审是否沉库 Kaleidoscope.UI） | `RecipeEditorPage.xaml` 资源 `RoiExpandButton`：纯图标方形"全屏标画"钮（四角扩展箭头 Path，30×30，圆角 9，hover 转 ThemePrimary）——库内 Kx*/Fresh* 按钮族六档均为「文字」或「图标+文字」胶囊，无纯图标方形按钮，故自绘；已满足 Theme* 键 + FocusVisualStyle=Null |
| 主题键新增（两套字典同键换值 + 契约键段字面值红线） | `Styles/ThemeLight.xaml` + `ThemeDark.xaml` 尾部注释 |
| 检测引擎接入（真实模型/HTTP 服务） | `AIris.Core\Detection\IDetectionEngine.cs` 文件头（接口契约 + HttpDetectionEngine 填实指引） |
| **判断某台机器能不能跑某张引擎卡 / Windows 部署形态选型** | `docs/AIris落地指南.md` **§2.9**（开发机实测速查、显存/架构门槛、Ollama 验链路、vLLM on Windows） |
| 引擎卡扩展（内置卡预设 / 添加自定义引擎 / 卡参数落盘） | `AIris.Core\Detection\EngineRegistry.cs`（Builtin 四卡）+ `AIris.Core\Store\EngineCardStore.cs`（Data\EngineCards.json）+ `Views\AddEngineDialog.xaml` |
| 相机接入（海康/大恒真机） | `AIris.Core\Camera\ICameraSource.cs` 文件头 + Kaleidoscope `Cameras/` 适配器 |
| 工步/判定规则语义 | `AIris.Core\Engine\WorkstepEngine.cs` + `AIris.Core\Judge\JudgeEngine.cs` 头注释（对应落地指南 §2.7） |
| 配方 JSON 结构 | `AIris.Core\Model\Recipe.cs` 头注释（含完整示例 JSON） |
| Mock 全流程演示脚本 | 本文「Mock 演示三件套」节 |
| submodule 提交顺序/库纪律 | `ThirdParty\Kaleidoscope\AGENTS.md` |
| 设计稿页面↔代码映射 | `docs/ui_design_mockups.html`（C 方案 data-page="monitor/recipe/editor/engine/audit/settings"） |

## 构建与验证命令

```powershell
# WPF 主工程（Core 自动级联；增量构建漏编新文件时删 Core obj/bin 后 -t:Restore,Build 整编）
& "D:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" "AIris.WPF\AIris.WPF.csproj" /p:Configuration=Debug /p:Platform=AnyCPU /t:Build /nologo /v:m
```

- 成功标准：`00_ExeBuild\AIris.WPF.exe` 生成且 0 error；启动主窗标题「AI 智瞳」出现。
- **冒烟**：启动 exe → 主窗出现 → 顶栏导航胶囊切 6 页 → 关闭。
- **全流程演示验收**：新建配方（向导两步）→ 启用 → 运行监控台启动检测 → 演示控制
  推进工步 → NG 模拟 → 告警与重检 → 审计页可查记录与截图存档。
- **Core 探针**：`powershell -File Tools\probe_ng_semantic.ps1`（NG 语义回归，改动 JudgeEngine
  后必跑）。
- **UI 目检**：`powershell -File Tools\shot_ui.ps1 -pages "monitor,recipe,engine,audit,settings"`
  （PrintWindow 不受窗口遮挡影响，截图落 `Tools\Shots\`）对照设计稿 C 方案（橙粉渐变/胶囊/
  大圆角/dock 导航/浅色默认），明暗两套各看一遍。
- **UI 交互实点回归**：`powershell -File Tools\click_ui.ps1 -x <坐标> -y <坐标>`（物理鼠标
  点击，验顶栏按钮/弹窗按钮等一切交互——静态截图验不出 WindowChrome 吞点击）+ agent-desktop
  截图确认状态变化；改顶栏/标题条后必跑。

## 文档同步（每次任务完成必做）

- **`CHANGELOG.md`**：顶部更新当前版本小节；没 commit 前同轮工作只追加 `###` 小节不加版本号。
- **代码注释**：详细到小白能看懂，中文 UTF-8，写完自查编码。
- **提交前自检**：`git status` + `git diff` 确认范围；用户不要求 commit 时只留工作区改动。
