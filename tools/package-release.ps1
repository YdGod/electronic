param([string]$OutputDirectory,[string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'dist' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $outputDirectory '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'DEVICE_SUPPORT.md') -Destination (Join-Path $outputDirectory '设备支持说明.md') -Force
$releaseFiles = @('BatteryPet.exe','LICENSE','NOTICE.md','使用说明.md','设备支持说明.md','Gsp370Mode.ps1','Enable-Gsp370-Direct.cmd','Restore-Epos.cmd','CpuTemperatureMode.ps1','Enable-Cpu-Temperature.cmd','Disable-Cpu-Temperature.cmd') | ForEach-Object { Join-Path $outputDirectory $_ }
$releaseFiles += @(Get-ChildItem -LiteralPath (Join-Path $outputDirectory 'devices\profiles'),(Join-Path $outputDirectory 'licenses') -Recurse -File | ForEach-Object FullName)
$thermalDirectory = Join-Path $outputDirectory 'thermal'
foreach ($required in @('BatteryPet.ThermalService.exe','LibreHardwareMonitorLib.dll','PawnIO_setup.exe','licenses\LHM-LICENSE.txt','licenses\PawnIO-LICENSE.txt','licenses\THIRD_PARTY.md','source\ThermalService.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $thermalDirectory $required))) { throw ('Missing thermal release component: '+$required) }
}
$releaseFiles += @(Get-ChildItem -LiteralPath $thermalDirectory -Recurse -File | ForEach-Object FullName)
$archivePath = if ($ArchivePath) { [IO.Path]::GetFullPath($ArchivePath) } else { Join-Path $projectRoot '小电量管家-便携版.zip' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
$package = [IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $releaseFiles) {
        $relativeName = $file.Substring($outputDirectory.Length+1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($package,$file,$relativeName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $package.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    if ($archive.Entries.Count -ne $releaseFiles.Count) { throw 'Unexpected release archive content.' }
    foreach ($entry in $archive.Entries) {
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $stream.Dispose(); $sha.Dispose() }
        $originalHash = (Get-FileHash -LiteralPath (Join-Path $outputDirectory $entry.FullName) -Algorithm SHA256).Hash
        if ($archiveHash -ne $originalHash) { throw ('Archive content does not match: '+$entry.FullName) }
    }
    $archive.Entries | Select-Object FullName,Length
}
finally { $archive.Dispose() }
