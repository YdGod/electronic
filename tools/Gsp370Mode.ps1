param([ValidateSet('Enable','Restore')][string]$Mode='Enable',[switch]$Administrator)
$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'BatteryPet.exe'
$statePath = Join-Path $PSScriptRoot 'epos-service-settings.json'
$resultPath = Join-Path $PSScriptRoot 'gsp370-mode-result.json'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Administrator) { throw '未取得管理员权限。' }
    $helper = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Mode',$Mode,'-Administrator') -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($helper.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $resultPath) { (Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json).Message | Write-Output }
        throw '未切换模式；请检查以上说明。'
    }
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 }
    Start-Process -FilePath $executable -ArgumentList '--show-devices' -WindowStyle Hidden
    return
}
$changedService = $false
try {
    $service = Get-Service -Name EPOSGamingSuiteService -ErrorAction Stop
    if ($Mode -eq 'Enable') {
        if (Get-Process -Name EPOSGamingSuite -ErrorAction SilentlyContinue) { throw '请先从系统托盘退出 EPOS Gaming Suite，再启用独立模式，避免软件重新启动服务。' }
        $saved = $null
        if (Test-Path -LiteralPath $statePath) { $saved = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json }
        if ($null -eq $saved -or -not $saved.Applied) {
            $key = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\EPOSGamingSuiteService'
            $saved = @{Start=$key.Start;DelayedAutoStart=$key.DelayedAutoStart;WasRunning=($service.Status -eq 'Running');Applied=$true}
            $saved | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
        }
        Set-Service -Name EPOSGamingSuiteService -StartupType Manual -ErrorAction Stop
        $changedService = $true
        if ($service.Status -eq 'Running') {
            $info = Get-CimInstance Win32_Service -Filter "Name='EPOSGamingSuiteService'"
            $serviceProcess = Get-Process -Id $info.ProcessId -ErrorAction Stop
            Stop-Service -Name EPOSGamingSuiteService -ErrorAction Stop
            $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(10))
            if (-not $serviceProcess.WaitForExit(10000)) { throw '服务已停止，但进程尚未退出；请稍后重试。' }
        }
        @{Success=$true;Message='已启用独立读取：EPOS 服务已停止并改为手动启动。';Mode=$Mode} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    }
    else {
        if (-not (Test-Path -LiteralPath $statePath)) { throw '没有保存的 EPOS 服务设置，无法自动恢复。' }
        $saved = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $startup = switch ([int]$saved.Start) { 2 {'Automatic'} 3 {'Manual'} 4 {'Disabled'} default {throw '保存的启动类型无效。'} }
        Set-Service -Name EPOSGamingSuiteService -StartupType $startup -ErrorAction Stop
        if ($null -ne $saved.DelayedAutoStart) { Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\EPOSGamingSuiteService' -Name DelayedAutoStart -Value ([int]$saved.DelayedAutoStart) -ErrorAction Stop }
        if ($saved.WasRunning) { Start-Service -Name EPOSGamingSuiteService -ErrorAction Stop }
        $saved.Applied=$false
        $saved | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
        @{Success=$true;Message='已恢复原来的 EPOS 服务启动设置。';Mode=$Mode} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    }
}
catch {
    $message = $_.Exception.Message
    if ($Mode -eq 'Enable' -and $changedService -and $null -ne $saved) {
        try {
            $startup = switch ([int]$saved.Start) { 2 {'Automatic'} 3 {'Manual'} 4 {'Disabled'} }
            Set-Service -Name EPOSGamingSuiteService -StartupType $startup -ErrorAction Stop
            if ($null -ne $saved.DelayedAutoStart) { Set-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\EPOSGamingSuiteService' -Name DelayedAutoStart -Value ([int]$saved.DelayedAutoStart) -ErrorAction Stop }
            if ($saved.WasRunning) { Start-Service -Name EPOSGamingSuiteService -ErrorAction Stop }
            $saved.Applied=$false
            $saved | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
            $message += ' 已恢复原来的 EPOS 服务设置。'
        }
        catch { $message += ' 自动恢复失败，请运行 Restore-Epos.cmd 恢复原设置。' }
    }
    @{Success=$false;Message=$message;Mode=$Mode} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    exit 1
}
