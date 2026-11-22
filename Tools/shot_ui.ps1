# AIris UI 目检截图（PrintWindow，不受窗口遮挡/前台影响；UTF-8 BOM + CRLF）
# 用法：powershell -File Tools\shot_ui.ps1 monitor,engine,audit,settings
# 原理：启动 exe → FindWindow("AI 智瞳") → 依次点击 dock 胶囊切页 → PrintWindow(PW_RENDERFULLCONTENT) 落 PNG 到 Tools\Shots\
param([string]$pages = "monitor,recipe,engine,audit,settings")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type '
using System;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  public struct RECT { public int L, T, R, B; }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}'
$bin = 'E:\Project\AIris\00_ExeBuild'
$outDir = 'E:\Project\AIris\Tools\Shots'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$p = Start-Process "$bin\AIris.WPF.exe" -PassThru
Start-Sleep -Seconds 9
$h = (Get-Process -Id $p.Id).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Start-Sleep -Seconds 3; $h = (Get-Process -Id $p.Id).MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { Stop-Process -Id $p.Id -Force; throw '未找到 AI 智瞳主窗' }
$null = [Cap]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 800

# dock 胶囊中心坐标（1920x1080 全屏最大化口径）：运行监控 700 / 配方中心 837 / 检测引擎 970 / 审计追溯 1100 / 设备集成 1234，y=1008
$navMap = @{ monitor = 700; recipe = 837; engine = 970; audit = 1100; settings = 1234 }

function Shot([string]$page) {
  if ($navMap.ContainsKey($page)) {
    [Cap]::Click([int]$navMap[$page], 1008)
    Start-Sleep -Seconds 2
    [Cap]::Click([int]$navMap[$page], 1008)   # 双保险：第一次点击可能被焦点变化吃掉
    Start-Sleep -Seconds 2
  }
  $r = New-Object Cap+RECT
  $null = [Cap]::GetWindowRect($h, [ref]$r)
  $w = [Math]::Max(100, [Math]::Min(4000, $r.R - $r.L))
  $ht = [Math]::Max(100, [Math]::Min(2400, $r.B - $r.T))
  Write-Output ("rect L=" + $r.L + " T=" + $r.T + " R=" + $r.R + " B=" + $r.B)
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
