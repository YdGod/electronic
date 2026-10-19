param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$executable = Join-Path $OutputDirectory 'BatteryPet.exe'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Test the supported background use without closing the official application or its service.
$officialProcess = Get-Process -Name EPOSGamingSuite -ErrorAction Stop | Select-Object -First 1
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$officialProcess.Id)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
$windowPattern = $null
$previousState = $null
foreach ($officialWindow in $windows) {
    $candidatePattern = $null
    if ($officialWindow.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$candidatePattern) -and $candidatePattern.Current.CanMinimize) {
        $windowPattern = [System.Windows.Automation.WindowPattern]$candidatePattern
        $previousState = $windowPattern.Current.WindowVisualState
        break
    }
}
if ($null -eq $windowPattern) { throw 'EPOS window does not expose a minimizable window pattern.' }
try {
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 300
    $diagnosticProcess = Start-Process -FilePath $executable -ArgumentList @('--diagnose',('"'+(Join-Path $OutputDirectory 'devices.json')+'"')) -WindowStyle Hidden -PassThru -Wait
    if ($diagnosticProcess.ExitCode -ne 0) { throw 'Device diagnostic failed.' }
    $diagnostic = Get-Content -LiteralPath (Join-Path $OutputDirectory 'devices.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $headset = $diagnostic.Devices | Where-Object { $_.Name -eq 'GSP 370' } | Select-Object -First 1
    if ($null -eq $headset.Percent -or $headset.Source -ne 'EPOS Gaming Suite') { throw 'GSP 370 could not be read while EPOS was minimized.' }
    if ($null -ne $headset.Charging -or $null -ne $headset.ReadAt) { throw 'The official UI display must not imply a known charging state or hardware timestamp.' }
    @{Passed=2;Checks=@('Official GSP 370 percentage remains readable while EPOS is minimized','Official display preserves unknown charging state and hardware timestamp');Percent=$headset.Percent} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'epos-background-test.json') -Encoding UTF8
    $diagnostic.Devices | Select-Object Name,Percent,Charging,Connected,Source | Format-Table -AutoSize
}
finally { $windowPattern.SetWindowVisualState($previousState) }

$widgetProcess = Start-Process -FilePath $executable -ArgumentList '--show-devices' -WindowStyle Hidden -PassThru
$widgetProcess.Id | Set-Content -LiteralPath (Join-Path $OutputDirectory 'widget-process.txt') -Encoding ASCII
Start-Sleep -Seconds 2
& (Join-Path $PSScriptRoot 'smoke-test.ps1') -WidgetProcessId $widgetProcess.Id -OutputDirectory $OutputDirectory -SkipInput
$renderProcess = Start-Process -FilePath $executable -ArgumentList @('--render-live',('"'+(Join-Path $OutputDirectory 'live-preview.png')+'"')) -WindowStyle Hidden -PassThru -Wait
if ($renderProcess.ExitCode -ne 0) { throw 'Live preview failed.' }
$showProcess = Start-Process -FilePath $executable -ArgumentList '--show-devices' -WindowStyle Hidden -PassThru -Wait
if ($showProcess.ExitCode -ne 0 -or @(Get-Process -Name BatteryPet).Count -ne 1) { throw 'Single instance verification failed.' }
Write-Output ('Widget remains running: '+$widgetProcess.Id)
