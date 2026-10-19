param([switch]$Test,[string]$OutputDirectory,[switch]$WithThermal)
$ErrorActionPreference = 'Stop'
$rootPath = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkPath 'csc.exe'))) {
    $frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $rootPath 'dist' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','System.Xaml.dll')
& (Join-Path $rootPath 'tools\validate-contributions.ps1')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/out:' + (Join-Path $outputPath 'BatteryPet.exe')),('/win32manifest:' + (Join-Path $rootPath 'app.manifest')))
foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $rootPath 'skins') -Filter 'skin.json' -Recurse -File | Sort-Object FullName) {
    $skin = Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $compilerArgs += '/resource:' + $manifest.FullName + ',BatteryPet.Skins.' + $skin.Id + '.skin.json'
    $frames = @($skin.Frames.Idle)
    if ($skin.Frames.Popup) { $frames += $skin.Frames.Popup }
    foreach ($frame in $frames | Select-Object -Unique) {
        $compilerArgs += '/resource:' + (Join-Path $manifest.DirectoryName $frame) + ',BatteryPet.Skins.' + $skin.Id + '.' + $frame
    }
}
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $frameworkPath $reference) }
foreach ($fixture in Get-ChildItem -LiteralPath (Join-Path $rootPath 'tests\fixtures') -Filter '*.json' -File) {
    $compilerArgs += '/resource:' + $fixture.FullName + ',BatteryPet.Fixtures.' + $fixture.Name
}
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $rootPath 'src'),(Join-Path $rootPath 'tests') -Filter '*.cs' -Recurse -File | Sort-Object FullName | ForEach-Object { $_.FullName })
& (Join-Path $frameworkPath 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $rootPath 'docs\USER_GUIDE.md') -Destination (Join-Path $outputPath '使用说明.md') -Force
New-Item -ItemType Directory -Path (Join-Path $outputPath 'devices\profiles') -Force | Out-Null
Copy-Item -Path (Join-Path $rootPath 'devices\profiles\*.json') -Destination (Join-Path $outputPath 'devices\profiles') -Force
foreach ($notice in @('LICENSE','NOTICE.md')) { Copy-Item -LiteralPath (Join-Path $rootPath $notice) -Destination $outputPath -Force }
New-Item -ItemType Directory -Path (Join-Path $outputPath 'licenses\skins') -Force | Out-Null
foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $rootPath 'skins') -Filter 'skin.json' -Recurse -File) {
    $noticePath = Join-Path $outputPath ('licenses\skins\' + $manifest.Directory.Name)
    New-Item -ItemType Directory -Path $noticePath -Force | Out-Null
    Copy-Item -LiteralPath $manifest.FullName -Destination $noticePath -Force
    if (Test-Path -LiteralPath (Join-Path $manifest.DirectoryName 'LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path $manifest.DirectoryName 'LICENSE.txt') -Destination $noticePath -Force
    }
}
Copy-Item -LiteralPath (Join-Path $rootPath 'DEVICE_SUPPORT.md') -Destination (Join-Path $outputPath '设备支持说明.md') -Force
$modeScript = Get-Content -LiteralPath (Join-Path $rootPath 'tools\Gsp370Mode.ps1') -Raw -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $outputPath 'Gsp370Mode.ps1'),$modeScript,(New-Object Text.UTF8Encoding($true)))
foreach ($launcher in @('Enable-Gsp370-Direct.cmd','Restore-Epos.cmd','Enable-Cpu-Temperature.cmd','Disable-Cpu-Temperature.cmd')) { Copy-Item -LiteralPath (Join-Path $rootPath ('tools\'+$launcher)) -Destination (Join-Path $outputPath $launcher) -Force }
$cpuModeScript = Get-Content -LiteralPath (Join-Path $rootPath 'tools\CpuTemperatureMode.ps1') -Raw -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $outputPath 'CpuTemperatureMode.ps1'),$cpuModeScript,(New-Object Text.UTF8Encoding($true)))
Write-Output ('Built: ' + (Join-Path $outputPath 'BatteryPet.exe'))
if ($WithThermal) { & (Join-Path $rootPath 'tools\build-thermal.ps1') -OutputDirectory (Join-Path $outputPath 'thermal') }
if ($Test) {
    $testProcess = Start-Process -FilePath (Join-Path $outputPath 'BatteryPet.exe') -ArgumentList @('--self-test', ('"' + (Join-Path $outputPath 'self-test.json') + '"')) -WindowStyle Hidden -PassThru -Wait
    $report = Get-Content -LiteralPath (Join-Path $outputPath 'self-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Output ('Self-test: ' + $report.Passed + ' passed; ' + @($report.Failures).Count + ' failed.')
    foreach ($failure in $report.Failures) { Write-Output ('FAILED: ' + $failure) }
    if ($testProcess.ExitCode -ne 0) { throw 'Self-test failed.' }
}
