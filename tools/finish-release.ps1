$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDirectory = Join-Path $projectRoot 'dist'
$executable = Join-Path $outputDirectory 'BatteryPet.exe'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReleaseWindowNative {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(IntPtr windowClass,string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
'@
$widgetId = [int](Get-Content -LiteralPath (Join-Path $outputDirectory 'widget-process.txt'))
$oldWidget = Get-Process -Id $widgetId -ErrorAction SilentlyContinue
if ($null -ne $oldWidget) {
    if ($oldWidget.MainModule.FileName -ne $executable) { throw 'The saved process ID no longer belongs to this widget.' }
    # The pet has no taskbar window, so Process.CloseMainWindow may not find it.
    $petWindow = [ReleaseWindowNative]::FindWindow([IntPtr]::Zero,'小电量管家')
    [uint32]$windowProcessId = 0
    [ReleaseWindowNative]::GetWindowThreadProcessId($petWindow,[ref]$windowProcessId) | Out-Null
    if ($petWindow -eq [IntPtr]::Zero -or $windowProcessId -ne $oldWidget.Id) { throw 'The pet window does not belong to the saved widget process.' }
    if (-not [ReleaseWindowNative]::PostMessage($petWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or -not $oldWidget.WaitForExit(3000)) { throw 'The widget could not be closed for its final build.' }
}
& (Join-Path $projectRoot 'build.ps1') -Test -WithThermal
$renderProcess = Start-Process -FilePath $executable -ArgumentList @('--render-live',('"'+(Join-Path $outputDirectory 'live-preview.png')+'"')) -WindowStyle Hidden -PassThru -Wait
if ($renderProcess.ExitCode -ne 0) { throw 'Live preview failed.' }
$exampleProcess = Start-Process -FilePath $executable -ArgumentList @('--render-preview',('"'+(Join-Path $outputDirectory 'preview.png')+'"')) -WindowStyle Hidden -PassThru -Wait
if ($exampleProcess.ExitCode -ne 0) { throw 'Example preview failed.' }
$diagnoseProcess = Start-Process -FilePath $executable -ArgumentList @('--diagnose',('"'+(Join-Path $outputDirectory 'devices.json')+'"')) -WindowStyle Hidden -PassThru -Wait
if ($diagnoseProcess.ExitCode -ne 0) { throw 'Live diagnosis failed.' }
& (Join-Path $PSScriptRoot 'package-release.ps1')

$newWidget = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
$newWidget.Id | Set-Content -LiteralPath (Join-Path $outputDirectory 'widget-process.txt') -Encoding ASCII
Write-Output ('Release ready; desktop widget process: '+$newWidget.Id)
