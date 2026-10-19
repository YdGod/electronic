$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = Join-Path $projectRoot 'research\skin-check'
$executable = Join-Path $projectRoot 'dist\BatteryPet.exe'
$wasRunning = @(Get-Process -Name BatteryPet -ErrorAction SilentlyContinue | Where-Object Path -eq $executable).Count -gt 0
$launched = $false
try {
    & (Join-Path $PSScriptRoot 'stop-widget.ps1')
    $test = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $PSScriptRoot 'verify-skins.ps1')+'"'),'-OutputDirectory',('"'+$stage+'"')) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $stage 'skin-ui-test.log') -RedirectStandardError (Join-Path $stage 'skin-ui-test-error.log')
    if ($test.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $stage 'skin-ui-test-error.log') -Encoding UTF8
        throw 'Skin UI verification failed; the previous executable is retained.'
    }
    Get-Content -LiteralPath (Join-Path $stage 'skin-ui-test.log') -Encoding UTF8
    & (Join-Path $projectRoot 'build.ps1') -OutputDirectory (Join-Path $projectRoot 'dist')
    & (Join-Path $PSScriptRoot 'package-release.ps1') | Out-Null
    $widget = Start-Process -FilePath $executable -WindowStyle Hidden -PassThru
    $widget.Id | Set-Content -LiteralPath (Join-Path $projectRoot 'dist\widget-process.txt') -Encoding ASCII
    $launched = $true
    Write-Output ('Updated release and portable archive; widget process: ' + $widget.Id)
}
finally {
    if (-not $launched -and $wasRunning) { Start-Process -FilePath $executable -WindowStyle Hidden | Out-Null }
}
