param([switch]$Administrator,[switch]$Adapter)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probeExecutable = Join-Path $projectRoot 'research\Gsp370DirectProbe.exe'
$resultPath = Join-Path $projectRoot 'dist\gsp370-direct-probe.json'
if ($Adapter) { $probeExecutable = Join-Path $projectRoot 'dist\BatteryPet.exe'; $resultPath = Join-Path $projectRoot 'dist\gsp370-direct.json' }
$adminLogPath = Join-Path $projectRoot 'dist\gsp370-admin-test.log'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Administrator) { throw 'An administrator session is required to temporarily release the official service port.' }
    $startedAt = Get-Date
    $normalWidgetPath = Join-Path $projectRoot 'dist\BatteryPet.exe'
    $normalWidgetWasRunning = @(Get-Process -Name BatteryPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $normalWidgetPath }).Count -gt 0
    $adminArguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Administrator')
    if ($Adapter) { $adminArguments += '-Adapter' }
    $adminProcess = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $adminArguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ((Test-Path -LiteralPath $resultPath) -and (Get-Item -LiteralPath $resultPath).LastWriteTime -ge $startedAt) { Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 }
    if ($adminProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $adminLogPath) { Get-Content -LiteralPath $adminLogPath -Encoding UTF8 -Tail 30 }
        throw 'The administrator test did not complete successfully.'
    }
    if ($normalWidgetWasRunning) {
        $restoredWidget = Start-Process -FilePath $normalWidgetPath -WindowStyle Hidden -PassThru
        $restoredWidget.Id | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\widget-process.txt') -Encoding ASCII
    }
    return
}
Start-Transcript -Path $adminLogPath -Force | Out-Null
$deviceKeyPath = 'HKLM:\SYSTEM\CurrentControlSet\Enum\USB\VID_1395&PID_00AB&MI_04'
$portName = $null
foreach ($instance in Get-ChildItem -LiteralPath $deviceKeyPath) {
    $parameters = Get-ItemProperty -LiteralPath (Join-Path $instance.PSPath 'Device Parameters') -Name PortName -ErrorAction SilentlyContinue
    if ($null -ne $parameters -and $parameters.PortName -match '^COM\d+$') { $portName = $parameters.PortName; break }
}
if ($null -eq $portName) { throw 'The GSP 370 receiver COM interface was not found.' }
$service = Get-Service -Name EPOSGamingSuiteService
$wasRunning = $service.Status -eq 'Running'
$environmentPath = Join-Path $projectRoot 'dist\gsp370-direct-environment.json'
$environmentReport = @{ Before = @(Get-CimInstance Win32_Service -Filter "Name='EPOSGamingSuiteService'" | Select-Object Name,State,ProcessId); AfterStop = $null; Restored = $null }
$widgetExecutable = Join-Path $projectRoot 'dist\BatteryPet.exe'
$widget = Get-Process -Name BatteryPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $widgetExecutable } | Select-Object -First 1
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ProbeWindowNative {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(IntPtr windowClass,string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
'@
$serviceProcess = $null
if ($wasRunning) {
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='EPOSGamingSuiteService'"
    $serviceProcess = Get-Process -Id $serviceInfo.ProcessId -ErrorAction Stop
}
try {
    if ($null -ne $widget) {
        $widgetWindow = [ProbeWindowNative]::FindWindow([IntPtr]::Zero,'小电量管家')
        [uint32]$windowProcessId = 0
        [ProbeWindowNative]::GetWindowThreadProcessId($widgetWindow,[ref]$windowProcessId) | Out-Null
        if ($windowProcessId -ne $widget.Id -or -not [ProbeWindowNative]::PostMessage($widgetWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or -not $widget.WaitForExit(3000)) { throw 'Could not pause the widget for an isolated query.' }
    }
    if ($wasRunning) {
        Stop-Service -Name EPOSGamingSuiteService -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(10))
        if (-not $serviceProcess.WaitForExit(10000)) { throw 'The stopped service process is still holding device resources.' }
    }
    $environmentReport.AfterStop = @(Get-CimInstance Win32_Service -Filter "Name='EPOSGamingSuiteService'" | Select-Object Name,State,ProcessId)
    $probeArguments = @($portName,('"'+$resultPath+'"'))
    if ($Adapter) { $probeArguments = @('--gsp370-direct',('"'+$resultPath+'"')) }
    $probeProcess = Start-Process -FilePath $probeExecutable -ArgumentList $probeArguments -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8
    if ($probeProcess.ExitCode -ne 0) {
        $ownerProcess = Start-Process -FilePath (Join-Path $projectRoot 'research\PortOwner.exe') -ArgumentList @($portName,('"'+(Join-Path $projectRoot 'dist\gsp370-port-owner-during-test.json')+'"')) -WindowStyle Hidden -PassThru -Wait
    }
    if ($probeProcess.ExitCode -ne 0) { throw 'Direct headset query failed.' }
    if ($Adapter) {
        $directReading = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($directReading.Source -ne 'GSP 370 直接查询' -or $null -eq $directReading.Percent -or $directReading.Connected -ne $true -or $null -eq $directReading.ReadAt) { throw 'The integrated adapter did not return a live receiver reading.' }
        $diagnosticPath = Join-Path $projectRoot 'dist\devices-direct.json'
        $diagnosticProcess = Start-Process -FilePath $probeExecutable -ArgumentList @('--diagnose',('"'+$diagnosticPath+'"')) -WindowStyle Hidden -PassThru -Wait
        if ($diagnosticProcess.ExitCode -ne 0) { throw 'The integrated provider scan failed.' }
        $scan = Get-Content -LiteralPath $diagnosticPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $headset = $scan.Devices | Where-Object { $_.Name -eq 'GSP 370' } | Select-Object -First 1
        if ($headset.Source -ne $directReading.Source -or $headset.Percent -ne $directReading.Percent) { throw 'The provider did not select the direct headset adapter.' }
        $scan.Devices | Select-Object Name,Percent,Source | ConvertTo-Json -Compress
    }
}
finally {
    if ($wasRunning) {
        Start-Service -Name EPOSGamingSuiteService -ErrorAction Stop
        (Get-Service -Name EPOSGamingSuiteService).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(10))
    }
    $environmentReport.Restored = @(Get-CimInstance Win32_Service -Filter "Name='EPOSGamingSuiteService'" | Select-Object Name,State,ProcessId)
    $environmentReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $environmentPath -Encoding UTF8
    if (-not $Administrator -and $null -ne $widget -and $widget.HasExited) {
        $restoredWidget = Start-Process -FilePath $widgetExecutable -WindowStyle Hidden -PassThru
        $restoredWidget.Id | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\widget-process.txt') -Encoding ASCII
    }
}
Get-Service -Name EPOSGamingSuiteService | Select-Object Name,Status | ConvertTo-Json -Compress
