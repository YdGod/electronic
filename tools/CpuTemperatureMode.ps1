param([ValidateSet('Enable','Disable')][string]$Mode = 'Enable',[switch]$Administrator)
$ErrorActionPreference = 'Stop'
$resultPath = Join-Path $PSScriptRoot 'cpu-temperature-mode-result.json'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Administrator) { throw '未取得管理员权限。' }
    $helper = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Mode',$Mode,'-Administrator') -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 }
    if ($helper.ExitCode -ne 0) { throw 'CPU 温度采集设置失败，详情见 cpu-temperature-mode-result.json。' }
    return
}
function Set-ProtectedFolder([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    foreach ($sidText in @('S-1-5-18','S-1-5-32-544','S-1-5-32-545')) {
        $sid = New-Object Security.Principal.SecurityIdentifier($sidText)
        $rights = if ($sidText -eq 'S-1-5-32-545') { [Security.AccessControl.FileSystemRights]::ReadAndExecute } else { [Security.AccessControl.FileSystemRights]::FullControl }
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid,$rights,$inheritance,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
    }
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}
try {
    $installRoot = Join-Path $env:ProgramFiles 'BatteryPet'
    $serviceDirectory = Join-Path $installRoot 'Thermal'
    $serviceExe = Join-Path $serviceDirectory 'BatteryPet.ThermalService.exe'
    $dataDirectory = Join-Path $env:ProgramData 'BatteryPet'
    $service = Get-Service -Name BatteryPetThermal -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        $existingPath = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\BatteryPetThermal').ImagePath
        if ($existingPath.Trim('"') -ne $serviceExe) { throw '同名服务的程序路径不属于本挂件，未修改。' }
    }
    if ($Mode -eq 'Disable') {
        if ($null -ne $service) { Stop-Service -Name BatteryPetThermal; Set-Service -Name BatteryPetThermal -StartupType Manual }
        @{Success=$true;Mode=$Mode;Message='已停止 CPU 温度采集，并改为手动启动；保留共享的 PawnIO 驱动。'} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
        return
    }
    $sourceDirectory = Join-Path $PSScriptRoot 'thermal'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'BatteryPet.ThermalService.exe'))) { throw '缺少 thermal 文件夹，请先完整解压便携包。' }
    $pawnKey = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
    if ($null -eq $pawnKey) {
        $installer = Join-Path $sourceDirectory 'PawnIO_setup.exe'
        $signature = Get-AuthenticodeSignature -LiteralPath $installer
        $expectedHash = 'A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0'
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu' -or (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) { throw 'PawnIO 官方安装器签名或校验值不匹配，未运行。' }
        $setup = Start-Process -FilePath $installer -ArgumentList @('-install','-silent') -WindowStyle Hidden -PassThru -Wait
        if ($setup.ExitCode -ne 0) { throw ('PawnIO 安装失败，返回码：'+$setup.ExitCode) }
    }
    if ($null -ne $service -and $service.Status -ne 'Stopped') { Stop-Service -Name BatteryPetThermal; $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(15)) }
    Set-ProtectedFolder $installRoot
    Set-ProtectedFolder $serviceDirectory
    Set-ProtectedFolder $dataDirectory
    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File) {
        if ($file.Extension -eq '.dll' -or $file.Name -eq 'BatteryPet.ThermalService.exe' -or $file.Name -eq 'BatteryPet.ThermalService.exe.config') { Copy-Item -LiteralPath $file.FullName -Destination $serviceDirectory -Force }
    }
    if ($null -eq $service) { New-Service -Name BatteryPetThermal -BinaryPathName ('"'+$serviceExe+'"') -DisplayName 'BatteryPet CPU Temperature' -Description 'On-demand CPU temperature requests for the BatteryPet desktop widget.' -StartupType Automatic -DependsOn PawnIO | Out-Null }
    else { Set-Service -Name BatteryPetThermal -StartupType Automatic }
    & (Join-Path $env:WINDIR 'System32\sc.exe') config BatteryPetThermal depend= PawnIO | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '无法设置 CPU 采集服务的驱动依赖。' }
    Start-Service -Name BatteryPetThermal
    $probePath = Join-Path $PSScriptRoot 'cpu-install-probe.json'
    $probe = Start-Process -FilePath (Join-Path $PSScriptRoot 'BatteryPet.exe') -ArgumentList @('--temperatures',('"'+$probePath+'"')) -WindowStyle Hidden -PassThru -Wait
    $reading = Get-Content -LiteralPath $probePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($probe.ExitCode -ne 0 -or @($reading.Readings | Where-Object { $_.Kind -eq 'CPU' -and $null -ne $_.Celsius }).Count -eq 0) {
        Stop-Service -Name BatteryPetThermal; Set-Service -Name BatteryPetThermal -StartupType Manual
        throw '采集服务未获得真实 CPU 温度，已停止并改为手动启动。'
    }
    @{Success=$true;Mode=$Mode;Message='已启用按需 CPU 采集：收到查询才采样，空闲时等待，不周期刷新；小人无需管理员权限。';Readings=$reading.Readings;ServiceDirectory=$serviceDirectory} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
catch {
    @{Success=$false;Mode=$Mode;Message=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    exit 1
}
