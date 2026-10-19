param([switch]$Administrator)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Administrator) { throw 'Administrator elevation failed.' }
    $helper = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Administrator') -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($helper.ExitCode -ne 0) { throw 'Service update failed; see dist result JSON files.' }
    return
}
$dist = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist'))
& (Join-Path $dist 'Gsp370Mode.ps1') -Mode Enable -Administrator
$gspResult = Get-Content -LiteralPath (Join-Path $dist 'gsp370-mode-result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $gspResult.Success) { throw $gspResult.Message }
& (Join-Path $dist 'CpuTemperatureMode.ps1') -Mode Enable -Administrator
$cpuResult = Get-Content -LiteralPath (Join-Path $dist 'cpu-temperature-mode-result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $cpuResult.Success) { throw $cpuResult.Message }
