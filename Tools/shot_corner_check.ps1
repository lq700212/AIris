# AIris 圆角/hover/弹窗回归截图（PrintWindow；临时验证脚本）
# 用法：powershell -File Tools\shot_corner_check.ps1
# 流程：启动(最大化) → 开 NG 弹窗截图 → 关弹窗 → 截全屏态 → 还原窗口化截图
#       → hover 🌓/min/max 截图 → 再最大化截图 → 退出
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type '
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Cap {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public struct RECT { public int L, T, R, B; }
  public static IntPtr Found;
  public static IntPtr FindByPrefix(string prefix) {
    Found = IntPtr.Zero;
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString().StartsWith(prefix)) { Found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return Found;
  }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); mouse_event(2,0,0,0,UIntPtr.Zero); mouse_event(4,0,0,0,UIntPtr.Zero); }
  public static void Hover(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(700); }
}'
$bin = 'E:\Project\AIris\00_ExeBuild'
$out = 'E:\Project\AIris\Tools\Shots'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out | Out-Null }

Get-Process AIris.WPF -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
$p = Start-Process "$bin\AIris.WPF.exe" -PassThru
Start-Sleep 9
$h = (Get-Process -Id $p.Id).MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Start-Sleep 3; $h = (Get-Process -Id $p.Id).MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { Stop-Process -Id $p.Id -Force; throw 'main window not found' }
$null = [Cap]::SetForegroundWindow($h); Start-Sleep 1

function ShotWin([IntPtr]$wh, [string]$name) {
  $r = New-Object Cap+RECT; $null = [Cap]::GetWindowRect($wh, [ref]$r)
  $w = [Math]::Max(50, $r.R - $r.L); $ht = [Math]::Max(50, $r.B - $r.T)
  $bmp = New-Object System.Drawing.Bitmap($w, $ht)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); $null = [Cap]::PrintWindow($wh, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save("$out\$name.png", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Write-Output ("saved $name.png rect L=$($r.L) T=$($r.T) R=$($r.R) B=$($r.B) size=${w}x${ht}")
}

function CornerAlpha([string]$name) {
  $bmp = New-Object System.Drawing.Bitmap("$out\$name.png")
  $w = [int]$bmp.Width; $ht = [int]$bmp.Height
  $a1 = $bmp.GetPixel(3,3).A; $a2 = $bmp.GetPixel($w-4,3).A
  $a3 = $bmp.GetPixel(3,$ht-4).A; $a4 = $bmp.GetPixel($w-4,$ht-4).A
  Write-Output ("$name corners A: TL=$a1 TR=$a2 BL=$a3 BR=$a4")
  $bmp.Dispose()
}

# —— 1. 全屏态：开 NG 详情弹窗（NG 挂起态 NG 详… 按钮，实机 1920 工作区坐标） ——
$r = New-Object Cap+RECT; $null = [Cap]::GetWindowRect($h, [ref]$r)
[Cap]::Click($r.L + 1204, $r.T + 323)   # NG 详…
Start-Sleep 2
$dlg = [Cap]::FindByPrefix('NG 详情')
if ($dlg -ne [IntPtr]::Zero) {
  ShotWin $dlg 'corner_dialog'
  $dr = New-Object Cap+RECT; $null = [Cap]::GetWindowRect($dlg, [ref]$dr)
  [Cap]::Click($dr.R - 150, $dr.B - 35)   # 关闭钮
  Start-Sleep 1
} else { Write-Output 'DIALOG NOT FOUND' }

# —— 2. 全屏态截图（应四角直角） ——
ShotWin $h 'corner_max'
CornerAlpha 'corner_max'

# —— 3. 还原 → 窗口化（圆角生效） ——
[Cap]::Click($r.R - 59, $r.T + 26)   # ▢ 最大化钮（全屏态）
Start-Sleep 2
$null = [Cap]::SetForegroundWindow($h); Start-Sleep 0.5
ShotWin $h 'corner_normal'
CornerAlpha 'corner_normal'
$r = New-Object Cap+RECT; $null = [Cap]::GetWindowRect($h, [ref]$r)

# —— 4. hover 三钮（窗口化态，白垫效果） ——
[Cap]::Hover($r.R - 143, $r.T + 26); ShotWin $h 'hover_theme'    # 🌓
[Cap]::Hover($r.R - 99, $r.T + 26);  ShotWin $h 'hover_min'      # —
[Cap]::Hover($r.R - 59, $r.T + 26);  ShotWin $h 'hover_max'      # ▢

# —— 5. 再最大化（圆角归零验证） ——
[Cap]::Click($r.R - 59, $r.T + 26)   # ▢ → 全屏
Start-Sleep 2
ShotWin $h 'corner_max_after'
CornerAlpha 'corner_max_after'

Stop-Process -Id $p.Id -Force
Write-Output 'ALL DONE'
