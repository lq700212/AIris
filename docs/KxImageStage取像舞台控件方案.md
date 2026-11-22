# KxImageStage 取像舞台控件方案（扫描动效 + 深色舞台 · 库级复用）

> 2026-11-22 定稿。本文档是**实施交接文档**：方案已与用户确认（含三色可配置细节），
> 下一步按「六、实施清单」逐项落地即可，无需重新调研。

## 一、背景与目标

用户三点诉求：

1. **取像显示区做出 UI 设计稿的扫描动效**——设计稿
   `docs/ui_design_mockups.html:578-594,730` 有完整定义：深色舞台底 `#060B16`
   + 18px 双向网格线（10% 蓝灰）+ 整面扫描光带（主色 10% 透明度、translateY
   上→下循环、4.2s linear infinite，注释原文"工业视觉的经典视觉语言"）。
2. **深色模式下取像区还是面板风格**——当前 `MonitorPage.xaml` 的 StageGrid
   无任何背景，深色下透出 `ThemePanelBg`（#271731 紫面板色），不是设计稿的
   "相机舞台"观感。HJVision-WPF 的取像区有独立深色舞台口径（空闲底 #060B16
   + 26px 网格，WinForms 自绘）可作参考。
3. **动效与舞台做在 Kaleidoscope 库的基类控件上**——其他项目引用直接就有，
   不重复造轮子。【用户追加】控件主体颜色（舞台底/网格线）与扫描颜色做成
   **可配置 DP**——其他项目可能要调原色，复用性更高。

## 二、摸底结论（已核实，勿重复探索）

| 事实 | 出处 |
|---|---|
| Kaleidoscope.UI **没有取像显示类控件**（只有窗基类/表单件/样式族），需新建 | 全库检索 `Image/Stage/Camera/Display/Preview` 仅命中相机设备类（无 UI）与 EyeIconView 图标 |
| AIris 取像区是纯 WPF（Image + Canvas 叠加），**无 HWND airspace 限制** | `AIris.WPF/Views/MonitorPage.xaml:45-62`（StageGrid/StageImage/OverlayCanvas） |
| HJVision 取像区是 WinForms 底座（Cognex ActiveX）自绘 | `HJVision-WPF/GYZVision.WPF/Views/VisionSurface.cs`；**KxImageStage 面向纯 WPF 项目，WinForms airspace 场景不适用**（登记边界） |
| 附加属性/DP 类先例 | `Kaleidoscope.UI/Views/KxRoundedClip.cs`（RegisterAttached + AffectsRender + Loaded/SizeChanged 单次挂钩） |
| 动画先例 | `ShellStyles.xaml:1194-1198` StatusDotPulse（keyed Storyboard + `x:Shared="False"` 防共享互斥）；HJVision 主窗呼吸白罩（DataTrigger 进出配对 RemoveStoryboard） |
| 库红线 | ①两块 Default 板（`Themes/DefaultLight.xaml` 170 行 / `DefaultDark.xaml` 131 行）**键集必须一致**；②新键宿主键板须同步供值（契约测试锁"库字典引用键 ⊆ 宿主键板键集"）；③缺键=DynamicResource 静默空白；④库代码控件全纯代码装配（无 Generic.xaml） |
| AIris 宿主主题 | `AIris.WPF/Styles/ThemeLight.xaml` + `ThemeDark.xaml` 整字典替换、键集一致、契约键值字面量直写 |
| AIris 无任何动画代码 | 全工程 grep Storyboard/DoubleAnimation 零命中 |

## 三、控件设计：`KxImageStage`

- **位置**：`ThirdParty/Kaleidoscope/Kaleidoscope.UI/Views/KxImageStage.cs`
- **命名空间**：`Kaleidoscope.UI.Views`（与库内其他控件一致）
- **继承**：`Grid` —— 宿主 XAML 子元素直接落入 Children，AIris 迁移零结构改动
  （`x:Name="StageGrid"` 保留，code-behind 的 ActualWidth/Height 坐标换算不动）
- **构造**：`ClipToBounds = true`（裁扫描带溢出）+ 建固定装饰层

### 3.1 层序（SetZIndex 锁死，不依赖 Children 顺序）

| ZIndex | 元素 | 说明 |
|---|---|---|
| -2 | `Border _bg`（IsHitTestVisible=false） | 舞台底：Background 绑定 `StageBackground` DP |
| -1 | `Rectangle _mesh`（IsHitTestVisible=false） | 网格底纹：DrawingBrush（Viewport=GridStep×GridStep Tile、1px 双向线），线画刷绑定 `GridLineBrush` DP |
| 0（默认） | 宿主 Children（相机画面/叠加框/空态） | 宿主内容天然夹在网格与扫描带之间 |
| 10 | `Rectangle _scanBand`（IsHitTestVisible=false） | 扫描光带：Height 绑控件 ActualHeight、HorizontalAlignment=Stretch、Fill 绑 `ScanBrush` DP、RenderTransform=TranslateTransform（Y 动画） |

> WPF 负 ZIndex 子元素画在 Panel Background 之上、普通子元素之下——正好承载装饰层。

### 3.2 三色可配置 DP（用户要求的核心）

| DP | 类型/默认 | 主题键兜底 | 语义 |
|---|---|---|---|
| `StageBackground` | Brush / null | `Brush.Shell.StageBg` | 舞台底画刷 |
| `GridLineBrush` | Brush / null | `Brush.Shell.StageGridLine` | 网格线画刷 |
| `ScanBrush` | Brush / null | `Brush.Shell.StageScan` | **整条扫描光带画刷**（渐变形状也可调，比单色更灵活） |

**机制**：DP 默认 null → 构造期 `SetResourceReference(XxxProperty, "Brush.Shell.xxx")`
（DynamicResource 跟随宿主深浅换肤）；宿主显式设 DP 本地值即覆盖资源引用
（WPF 本地值优先级高于资源引用），内部元素 Binding 到 DP 自动跟随——
**不配 = 主题自动配色，配了 = 项目自定义原色**。

内部元素接线用代码 Binding（装饰层元素不是 TemplateBinding 场景）：

```csharp
_bg.SetBinding(Border.BackgroundProperty, new Binding(nameof(StageBackground)) { Source = this });
_mesh Pen.Brush / _scanBand.Fill 同理（Pen.Brush 是 DP，可直接 SetBinding）
```

### 3.3 行为 DP

| DP | 默认 | 说明 |
|---|---|---|
| `ScanEnabled` | true | 实例级开关；**有效开关 = `ScanEnabled && KxShellDefaults.EnableStageScanMotion`** |
| `GridStep` | 18 | 网格步距 px（设计稿口径；HJVision 是 26，可配）；变化回调重建 DrawingBrush Viewport |
| `ScanDuration` | 4.2 | 扫描一轮秒数 |

### 3.4 扫描动效（代码 Storyboard，每实例独立）

- `DoubleAnimation` 驱动 `TranslateTransform.Y`：From=`-ActualHeight` → To=`+ActualHeight`，
  `Duration=TimeSpan.FromSeconds(ScanDuration)`、linear（无 Easing）、`RepeatBehavior=Forever`；
- `SizeChanged` 重建动画参数（先 Remove 旧 Storyboard 再新建——KxRoundedClip 的
  SizeChanged 重设几何同口径）；
- **生命周期防空转**（HJVision 红线口径"仅运行态小面积动画、停机 RemoveStoryboard"）：
  `IsVisible=false` / `Unloaded` 时 `Storyboard.Remove`，恢复可见/Loaded 且有效开关为真再 Begin
  （后台页签不可见不空转——分层窗软件渲染下每帧重绘整窗，这是性能护栏）；
- 每实例代码建 Storyboard，无 keyed 资源共享互斥问题（StatusDotPulse 的
  x:Shared=False 是 XAML 资源场景的等价解法）。

### 3.5 总闸（库级）

`KxShellDefaults.cs`（`Kaleidoscope.UI/Views/KxShellDefaults.cs`）加：

```csharp
/// <summary>取像舞台扫描动效总闸（缺省 true）。宿主可一键关全局动效
/// （如工控现场低配机/软件渲染分层窗性能受限场景）；实例级还有 ScanEnabled。</summary>
public static bool EnableStageScanMotion { get; set; } = true;
```

### 3.6 软渲染性能护栏（为什么这样做不会卡）

AIris 主窗是 AllowsTransparency 分层窗 = 软件渲染。但：①扫描带是单条 10%
透明度渐变带，非大面积不透明动画；②IsVisible/Unloaded 双停 + 总闸兜底。
库内"transform 上浮不做"红线（ShellStyles.xaml:66-67）针对的是按钮 hover 交互
反馈，不禁止本类装饰性位移动画（设计稿原始口径即 translateY 循环）。

**【实施后实测修正（2026-11-22 验证轮，推翻本节初版论证）】**初版认为"Mock 8fps
帧重绘已存在、动画增量与之同量级"——**实测错了**：WPF 动画时钟缺省 60fps，
每 tick 都触发分层窗整窗重绘，60fps vs 8fps 差 7 倍以上。屏摄验证期间
`Get-Process` CPU 增量采样：动效开启稳态 **~85-90% 单核占用**（暂停 Mock 后
仍 ~89%，最小化 IsVisible=false 后骤降 **~15%**——同时实锤生命周期双停护栏
有效 + 定位主因是动画时钟）。**修复 = `Timeline.SetDesiredFrameRate(anim, 10)`
帧率降档**：扫描带是慢位移（4.2s 一轮，10fps 每帧位移 ~38px@800px 舞台，
肉眼平滑无跳感），修复后稳态 **~50% 单核**（其中 Mock 合帧线程 ~15%、
Mock 8fps 帧流全窗重绘 ~25% 为 V0.1.0 固有，动画纯增量 ~10%）。**通用口径：
分层窗软渲染宿主的一切 Decorative 动画必须设 DesiredFrameRate（≤10fps 量级），
60fps 时钟在软件渲染下不可接受**——已随实现写入 KxImageStage.cs 代码注释。

### 3.7 V2.3.6 帧显示管线（傻瓜式升级 + 扫描联动契约）

**背景（用户第八~九轮反馈）**：①红框截图发现"画面矩形内有台面网格，letterbox
露出舞台小网格"两层叠加——根因是 Mock 合成帧自画台面（暖白渐变底 + 64px 网格
+ 水印），真实监控视频不会带装饰，舞台一层网格+扫描才对；②取像控件要向
HJVision 的 VisionPreviewHost 学习"傻瓜式复用"——黑盒逻辑全在控件内，宿主
一行绑定即得完整取像显示；③**显示图像/实时视频时自动关闭扫描动效，只有初始
空态（无帧）才有扫描**。

**控件升级（Kaleidoscope.UI V2.3.6）**：
- 内置三件套：帧图 `Image`（ZIndex=0）/ 叠加画布 `public Canvas Overlay`
  （ZIndex=5，画面上扫描带下）/ 空态文本（弱化前景键 Brush.Shell.FgDim）；
- 双喂帧入口（二选一红线，同实例禁混用）：
  绑定式 `FrameSource="{Binding FrameImage}"`（MVVM 口径）；
  命令式 `ShowFrame(ImageSource)/ClearFrame()`（**任意线程安全**，内部
  Dispatcher 封送，帧线程直调不崩——对标 HJVision"检测线程直调"口径）；
- `IdleText` 空态文本 DP（有帧自动隐藏、无帧自动回来）；`FrameStretch`
  拉伸 DP（默认 Uniform，letterbox 对齐网格舞台）；
- **扫描联动（封装内建，宿主零接线）**：有效开关内含 `FrameSource == null`
  ——有帧自动停（带子收起+清动画时钟），清帧回空态自动恢复。扫描只属于
  "初始空态"，画面/视频不被光带干扰，帧流高频重绘期动画时钟也不再叠加开销；
- 纯装饰用法零破坏：不喂帧 = 内置三件全 Collapsed，V2.3.5 用法原样可用。

**宿主配套（AIris V0.1.5）**：
- Mock 帧透明化（`MockCameraSource.Compose` 去台面渐变底 + 64px 网格 + 水印，
  只画零件；文字抗锯齿 ClearType→AntiAliasGridFit 防透明底脏边）——
  全舞台"一层网格+扫描"，letterbox 与空态露网格、零件浮在舞台上；
- `Imaging.ToBitmapSource` 弃 GetHbitmap 路线（HBitmap 无 alpha 语义，透明
  被压黑底）改 LockBits 直拷；**alpha 语义红线：GDI Format32bppArgb = 直通
  alpha，目标格式必须 `PixelFormats.Bgra32`（直通）——喂 Pbgra32（预乘）会把
  半透明像素当预乘值解读，显示叠底溢出爆白**（实测：透明帧零件区全糊白）；
- MonitorPage 傻瓜式迁移：手工 Image/空态文本/OverlayCanvas 三件删除，
  一行 `FrameSource="{Binding FrameImage}" IdleText="等待画面…"`；叠加框
  code-behind 取 `Stage.Overlay` 画（Stage 别名=StageGrid，坐标口径零改动）。

**验证（2026-11-22）**：整编 0 error；浅色屏摄逐位——舞台底 #E9EEF8 命中、
18px 网格透底、透明帧零件浮层清晰、无第二层台面；有帧连拍 diff=0（扫描停
实锤，同参数 V0.1.4 纯装饰版连拍 diff 显著）；"空场透明帧也算有帧"（Mock
相机 8fps 持续出帧）故本工程演示链路空态扫描只在"未启动检测"前出现——
契约正确执行。存档链路（SaveShot 直存 Bitmap）alpha 保留实测（零件半透明
像素 A38/底 A0）。

## 四、主题键契约（库两板 + 宿主两板，四处同加）

### 4.1 库缺省板（`Themes/DefaultLight.xaml` + `DefaultDark.xaml`，两板键集一致）

在各自 "Shell 族" 段尾（`SysBtnDisabled` 之后）追加，值字面量直写：

```xml
<!-- 取像舞台（V2.3.5）：相机画面显示区的"舞台"三件（底/网格/扫描光带） -->
<SolidColorBrush x:Key="Brush.Shell.StageBg" Color="#E9EEF8"/>          <!-- Light；Dark=#060B16 -->
<SolidColorBrush x:Key="Brush.Shell.StageGridLine" Color="#1A5064A0"/> <!-- Light；Dark=#1A7E96BE -->
<LinearGradientBrush x:Key="Brush.Shell.StageScan" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#001E90FF" Offset="0"/>   <!-- Light 主色 #1E90FF 的 0α/10%/0α 三停靠 -->
    <GradientStop Color="#1A1E90FF" Offset="0.46"/>
    <GradientStop Color="#001E90FF" Offset="0.92"/>
</LinearGradientBrush>
<!-- Dark 板主色 #FF2E8FE0 → 三停靠 #002E8FE0 / #1A2E8FE0 / #002E8FE0 -->
```

- 色值口径 = 设计稿 `.cam-img`（dark #060b16 / light #e9eef8）与 `--grid`
  （dark rgba(126,150,190,.10) / light rgba(80,100,160,.10)）；扫描带 =
  各板主色 10%（0.46 停靠点、92% 渐隐，照设计稿 `sScan` 的渐变比例）。
- **纯 WPF 无 HWND 丢 α 问题，半透明直写即可**（HJVision 的"网格线预混实色"
  红线是 WinForms/HWND 合成场景专属，不适用本控件）。
- 两板头注释的键名清单同步登记（改动须全库回归的契约清单）。

### 4.2 AIris 宿主键板（`AIris.WPF/Styles/ThemeLight.xaml` + `ThemeDark.xaml`）

同加三键 C 方案覆盖值（满足"宿主键板 ⊇ 库引用键"契约测试）：

```xml
<!-- 取像舞台三键（库 KxImageStage 契约） -->
<SolidColorBrush x:Key="Brush.Shell.StageBg" Color="#E9EEF8"/>          <!-- Dark=#060B16 -->
<SolidColorBrush x:Key="Brush.Shell.StageGridLine" Color="#1A5064A0"/> <!-- Dark=#1A7E96BE -->
<LinearGradientBrush x:Key="Brush.Shell.StageScan" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#00F97316" Offset="0"/>   <!-- C 方案主色 #F97316 两板同值 -->
    <GradientStop Color="#1AF97316" Offset="0.46"/>
    <GradientStop Color="#00F97316" Offset="0.92"/>
</LinearGradientBrush>
```

## 五、AIris 宿主迁移（零逻辑改动）

`AIris.WPF/Views/MonitorPage.xaml`：

1. UserControl 加 xmlns：`xmlns:ui="clr-namespace:Kaleidoscope.UI.Views;assembly=Kaleidoscope.UI"`
2. 行 45 的 `<Grid Grid.Row="0" x:Name="StageGrid" ClipToBounds="True" SizeChanged="Stage_SizeChanged">`
   换成 `<ui:KxImageStage Grid.Row="0" x:Name="StageGrid" SizeChanged="Stage_SizeChanged">`
   （ClipToBounds 控件构造已设；三子元素 StageImage/OverlayCanvas/空态 TextBlock 原样保留）；
3. **`MonitorPage.xaml.cs` 零改动**——KxImageStage 即 Grid，ActualWidth/ActualHeight
   与 SizeChanged 事件同源，RedrawBoxes 的 4:3 内接矩形坐标换算不受影响。

## 六、实施清单（按序执行）

| # | 动作 | 文件 |
|---|---|---|
| 1 | 新建控件（完整实现见第三节设计） | `ThirdParty/Kaleidoscope/Kaleidoscope.UI/Views/KxImageStage.cs` |
| 2 | 加总闸 `EnableStageScanMotion` | `ThirdParty/Kaleidoscope/Kaleidoscope.UI/Views/KxShellDefaults.cs` |
| 3 | 两板同加三键 + 头注释键名清单登记 | `ThirdParty/Kaleidoscope/Kaleidoscope.UI/Themes/DefaultLight.xaml`、`DefaultDark.xaml` |
| 4 | 控件复用索引场景表登记 + 适用边界 | `ThirdParty/Kaleidoscope/Kaleidoscope.UI/README.md` |
| 5 | 库 CHANGELOG 加 V2.3.5 条目 | `ThirdParty/Kaleidoscope/CHANGELOG.md` |
| 6 | 宿主两板同加三键（4.2 节值） | `AIris.WPF/Styles/ThemeLight.xaml`、`ThemeDark.xaml` |
| 7 | StageGrid 换 `ui:KxImageStage`（第五节） | `AIris.WPF/Views/MonitorPage.xaml` |
| 8 | 主仓 CHANGELOG 加 V0.1.4 条目 | `AIris/CHANGELOG.md` |
| 9 | 编译 + 截图验证（第七节） | —— |

> 提交纪律：子仓先 commit/push（V2.3.5），主仓再更新 submodule 指针提交（V0.1.4），
> 全程 `--explicit` 显式暂存；**本轮先不动 git，等用户指令**。

## 七、验证清单

1. 编译 AIris.sln 0 error（库随 ProjectReference 连编）。
2. 起 exe（00_ExeBuild）：窗口化 → **屏摄**（CopyFromScreen；PrintWindow 对分层窗
   透明区填黑失真，四角/透明区验证必须屏摄）：
   - 深色模式（顶栏 🌓 在 R-143, y=26）：舞台底 #060B16 + 18px 网格 + 橙色扫描光带可见；
   - 浅色模式：舞台底 #E9EEF8 + 网格 + 扫描光带可见；
3. **动效验证**：间隔约 2.1s 连拍两张（4.2s 一轮的半周期，扫描带位置应明显位移），
   两张对比确认在动；
4. 回归：检测框叠加位置正确（Mock 放件后框贴合）、空态"等待画面…"文本、
   NG 详情弹窗圆角不受影响、深浅切换后舞台色跟随；
5. 临时截图脚本**必须 UTF-8 BOM**（无 BOM 按 GBK 解析中文串 → FindWindow 静默失败，
   V0.1.3 实坑）；可参考 `Tools/shot_corner_check.ps1` 的结构。

## 八、验收口径（用户视角）

- 深色模式下取像区不再是紫色面板，而是设计稿的深色"相机舞台"（深底 + 网格）；
- 画面上有一条缓慢循环的主色扫描光带（4.2s 一轮，浅色模式同样有）；
- 其他项目引用 Kaleidoscope.UI 后一行标签即可获得同款动效；调原色只需设
  `StageBackground` / `GridLineBrush` / `ScanBrush` 三个属性，或换宿主键板三键。
