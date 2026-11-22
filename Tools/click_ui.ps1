# click_ui.ps1 - physical mouse click at absolute screen coordinates
# (same input path as a real user click: goes through WindowChrome hit-test)
# Usage: powershell -File Tools\click_ui.ps1 -x 344 -y 26
param([int]$x = 0, [int]$y = 0)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@

[Mouse]::SetCursorPos($x, $y) | Out-Null
Start-Sleep -Milliseconds 150
[Mouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # MOUSEEVENTF_LEFTDOWN
Start-Sleep -Milliseconds 60
[Mouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # MOUSEEVENTF_LEFTUP
Write-Output ("clicked {0},{1}" -f $x, $y)
