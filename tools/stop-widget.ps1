$ErrorActionPreference = 'Stop'
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist\BatteryPet.exe'))
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StopPetNative {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(IntPtr windowClass,string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
'@
$petWindow = [StopPetNative]::FindWindow([IntPtr]::Zero,'小电量管家')
if ($petWindow -eq [IntPtr]::Zero) {
    if (Get-Process -Name BatteryPet -ErrorAction SilentlyContinue | Where-Object Path -eq $executable) { throw 'Widget process is running but its desktop window could not be found.' }
    return
}
[uint32]$widgetId = 0
[StopPetNative]::GetWindowThreadProcessId($petWindow,[ref]$widgetId) | Out-Null
$widget = Get-Process -Id $widgetId
if ($widget.Path -ne $executable) { throw 'The window is not owned by this project executable.' }
if (-not [StopPetNative]::PostMessage($petWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or -not $widget.WaitForExit(5000)) { throw 'Widget could not be closed cleanly.' }
Write-Output ('Closed widget '+$widgetId)
