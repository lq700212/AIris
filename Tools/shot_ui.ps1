# AIris UI 目检截图（PrintWindow，不受窗口遮挡/前台影响；UTF-8 BOM + CRLF）
# 用法：powershell -File Tools\shot_ui.ps1 monitor,recipe,engine,audit,settings,editor
# 可选输出目录：powershell -File Tools\shot_ui.ps1 audit -outDir C:\Temp\shots（默认 Tools\Shots）
#
# 原理：启动 exe → 最大化（统一 1920×1040 目检口径）→ UIA 取顶栏导航胶囊中心
#       → 依次点击切页（editor = 配方中心 + 第一张卡「编辑」）→ PrintWindow 落 PNG。
#
# 【2026-11-23 坐标修复】顶栏化之后导航胶囊从底部 Dock 移到顶栏中央，旧窗口内坐标
# （monitor=222 recipe=352 engine=481 audit=619 settings=749, y=21）全部失效——点到的是
# 品牌区/空白，导致 audit.png 与 settings.png 内容重复。本次改为：
#   1) 进程启动即 SetProcessDPIAware()：GetWindowRect / SetCursorPos / UIA 统一物理像素，
#      否则 125%/150% 缩放下坐标系不一致，点击整体偏移；
#   2) 启动后 SW_MAXIMIZE：与 README/文档配图的 1920×1040 口径一致；
#   3) 优先 UIA：按「名称 + RadioButton」查胶囊 BoundingRectangle 取中心——窗口尺寸、
#      DPI、字体缩放变化时也永远点得准，坐标不会再次过期；
#   4) UIA 不可用时回落硬编码基准（1920×1040 实测），按「导航整体水平居中 + 当前 DPI」
#      换算——导航居中，居中偏移量不随窗口宽度变化。
param(
  [string]$pages = "monitor,recipe,engine,audit,settings",
  [string]$outDir = "E:\Project\AIris\Tools\Shots"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# UIA 用于按控件名称定位导航胶囊（拿不到就回落硬编码坐标）
$uiaOk = $true
try {
  Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
  Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop
} catch { $uiaOk = $false }

Add-Type '
using System;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
  public struct RECT { public int L, T, R, B; }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}'
$null = [Cap]::SetProcessDPIAware()

$bin = 'E:\Project\AIris\00_ExeBuild'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# 每次目检都重启一个干净实例：避免已有实例导致取到旧窗口/旧页面状态
Get-Process AIris.WPF -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$p = Start-Process "$bin\AIris.WPF.exe" -PassThru
Start-Sleep -Seconds 9
$h = (Get-Process -Id $p.Id).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Start-Sleep -Seconds 3; $h = (Get-Process -Id $p.Id).MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { Stop-Process -Id $p.Id -Force; throw '未找到 AI 智瞳主窗' }

# 统一口径：最大化（设计基准 / README 配图全部是最大化 1920×1040）
$null = [Cap]::ShowWindow($h, 3)   # 3 = SW_MAXIMIZE
Start-Sleep -Seconds 2
$null = [Cap]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 800

$wr = New-Object Cap+RECT
$null = [Cap]::GetWindowRect($h, [ref]$wr)
Write-Output ("window L=" + $wr.L + " T=" + $wr.T + " size=" + ($wr.R - $wr.L) + "x" + ($wr.B - $wr.T) + " uia=" + $uiaOk)

# 顶栏导航胶囊：UIA 名称 → 设计顺序（对位设计稿 .navpill 五项）
$navNames = @{
  monitor  = '运行监控'
  recipe   = '配方中心'
  engine   = '检测引擎'
  audit    = '审计追溯'
  settings = '设备与集成'
}

# UIA 不可用时的回落基准：1920×1040 最大化、导航整体居中时的屏幕物理坐标（实测）
# 换算规则：水平 = 窗口中心 + (基准 - 960) × DPI 比例；垂直 = 窗口顶 + 26 × DPI 比例
$navFallback = @{ monitor = 680; recipe = 779; engine = 880; audit = 981; settings = 1089 }

# 配方中心第一张卡片「编辑」按钮（1920×1040 实测，窗口内物理坐标）
$editBtnBase = @{ x = 63; y = 224 }

function GetDpiScale {
  try {
    $dpi = [Cap]::GetDpiForWindow($h)
    if ($dpi -gt 0) { return $dpi / 96.0 }
  } catch { }
  return 1.0
}

function GetNavPoint([string]$key) {
  if ($uiaOk) {
    try {
      $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
      $cName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navNames[$key])
      $cType = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
      $cond = New-Object System.Windows.Automation.AndCondition -ArgumentList (, ([System.Windows.Automation.Condition[]]@($cName, $cType)))
      $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
      if ($null -ne $el) {
        $r = $el.Current.BoundingRectangle
        if ($r.Width -gt 4 -and $r.Height -gt 4) {
          return @{ x = [int]($r.Left + $r.Width / 2); y = [int]($r.Top + $r.Height / 2); src = 'uia' }
        }
      }
    } catch { }
  }
  # 回落：导航整体水平居中，居中偏移不随窗口宽度变；纵向按 DPI 缩放
  $scale = GetDpiScale
  $r2 = New-Object Cap+RECT
  $null = [Cap]::GetWindowRect($h, [ref]$r2)
  $centerX = ($r2.L + $r2.R) / 2.0
  return @{ x = [int]($centerX + ($navFallback[$key] - 960) * $scale); y = [int]($r2.T + 26 * $scale); src = 'fallback' }
}

function ClickNav([string]$key) {
  if (-not $navNames.ContainsKey($key)) { throw ("未知页面：" + $key) }
  $pt = GetNavPoint $key
  Write-Output ("nav " + $key + " -> " + $pt.x + "," + $pt.y + " (" + $pt.src + ")")
  $null = [Cap]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 250
  [Cap]::Click($pt.x, $pt.y)
  Start-Sleep -Milliseconds 400
  # 双保险：窗口不在前台时首次点击可能只用来激活窗口
  [Cap]::Click($pt.x, $pt.y)
  Start-Sleep -Seconds 2
}

function Shot([string]$page) {
  if ($page -eq 'editor') {
    # 编辑器没有顶栏胶囊入口：先切配方中心，再点第一张卡片的「编辑」
    ClickNav 'recipe'
    $scale = GetDpiScale
    $r2 = New-Object Cap+RECT
    $null = [Cap]::GetWindowRect($h, [ref]$r2)
    $ex = [int]($r2.L + $editBtnBase.x * $scale)
    $ey = [int]($r2.T + $editBtnBase.y * $scale)
    Write-Output ("edit-btn -> " + $ex + "," + $ey)
    [Cap]::Click($ex, $ey)
    Start-Sleep -Seconds 3
  }
  elseif ($navNames.ContainsKey($page)) {
    ClickNav $page
  }
  else {
    throw ("未知页面：" + $page + "（可用：monitor/recipe/engine/audit/settings/editor）")
  }

  $r = New-Object Cap+RECT
  $null = [Cap]::GetWindowRect($h, [ref]$r)
  $w = [Math]::Max(100, [Math]::Min(4000, $r.R - $r.L))
  $ht = [Math]::Max(100, [Math]::Min(2400, $r.B - $r.T))
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  $null = [Cap]::PrintWindow($h, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc)
  $g.Dispose()
  $bmp.Save("$outDir\$page.png", [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output ("saved $page.png " + $w + "x" + $ht)
}

foreach ($pg in $pages.Split(',')) { Shot $pg.Trim() }
Stop-Process -Id $p.Id -Force
Write-Output 'ALL DONE'
