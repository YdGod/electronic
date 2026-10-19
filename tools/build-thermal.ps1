param([string]$OutputDirectory,[string]$LibraryDirectory,[string]$PawnInstaller)
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $rootPath 'dist\thermal' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$libraryPath = if ($LibraryDirectory) { [IO.Path]::GetFullPath($LibraryDirectory) } else { Join-Path $rootPath 'vendor\LibreHardwareMonitor' }
$installerPath = if ($PawnInstaller) { [IO.Path]::GetFullPath($PawnInstaller) } else { Join-Path $rootPath 'vendor\PawnIO\PawnIO_setup.exe' }
# This helper enables only the CPU group; storage, HID and the upstream GUI are not loaded.
$cpuLibraries = @('LibreHardwareMonitorLib.dll','System.Memory.dll','System.Runtime.CompilerServices.Unsafe.dll','System.Buffers.dll','System.Numerics.Vectors.dll')
foreach ($library in $cpuLibraries) {
    if (-not (Test-Path -LiteralPath (Join-Path $libraryPath $library))) { throw ('Missing thermal dependency: ' + $library + '. See docs/BUILDING.md.') }
}
if (-not (Test-Path -LiteralPath $installerPath)) { throw 'Missing PawnIO installer. See docs/BUILDING.md.' }
if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash -ne 'A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0') { throw 'PawnIO installer SHA256 does not match the pinned 2.1.0 release.' }
foreach ($library in $cpuLibraries) { Copy-Item -LiteralPath (Join-Path $libraryPath $library) -Destination $outputPath -Force }
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output',('/out:' + (Join-Path $outputPath 'BatteryPet.ThermalService.exe')),('/reference:' + (Join-Path $outputPath 'LibreHardwareMonitorLib.dll')))
foreach ($reference in @('System.dll','System.Core.dll','System.ServiceProcess.dll','System.Runtime.Serialization.dll')) { $compilerArgs += '/reference:' + (Join-Path $frameworkPath $reference) }
$compilerArgs += (Join-Path $rootPath 'services\thermal\ThermalService.cs'), (Join-Path $rootPath 'src\Core\TemperatureModels.cs')
& (Join-Path $frameworkPath 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Thermal service build failed.' }
Copy-Item -LiteralPath (Join-Path $rootPath 'services\thermal\BatteryPet.ThermalService.exe.config') -Destination $outputPath -Force
$licensePath = Join-Path $outputPath 'licenses'
New-Item -ItemType Directory -Path $licensePath -Force | Out-Null
foreach ($license in @('LHM-LICENSE.txt','PawnIO-COPYING.txt','LHM-THIRD-PARTY-NOTICES.txt','PawnIO-README.md','PawnIO-LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $rootPath ('third_party\licenses\'+$license)) -Destination $licensePath -Force
}
Copy-Item -LiteralPath (Join-Path $rootPath 'third_party\THIRD_PARTY.md') -Destination $licensePath -Force
$sourcePath = Join-Path $outputPath 'source'
New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $rootPath 'services\thermal\ThermalService.cs'),(Join-Path $rootPath 'src\Core\TemperatureModels.cs') -Destination $sourcePath -Force
Copy-Item -LiteralPath $installerPath -Destination (Join-Path $outputPath 'PawnIO_setup.exe') -Force
