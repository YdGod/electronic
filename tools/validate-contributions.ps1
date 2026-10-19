param([string]$RootPath = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))))
$ErrorActionPreference = 'Stop'
$count = 0
$ids = @{}
foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $RootPath 'skins') -Filter skin.json -Recurse -File) {
    if ($manifest.Length -gt 65536) { throw "Skin manifest exceeds 64 KB: $($manifest.FullName)" }
    $skin = Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($skin.SchemaVersion -ne 1 -or $skin.Id -cnotmatch '\A[a-z][a-z0-9-]{0,63}\z' -or $skin.Id -cne $manifest.Directory.Name -or $manifest.Directory.Parent.FullName -ne (Join-Path $RootPath 'skins')) { throw "Invalid skin version, id or directory: $($manifest.FullName)" }
    foreach ($field in @('Name','Author','License')) { if ([string]::IsNullOrWhiteSpace($skin.$field)) { throw "Missing skin $field : $($manifest.FullName)" } }
    if ($ids.ContainsKey($skin.Id)) { throw "Duplicate skin id: $($skin.Id)" }
    $ids[$skin.Id] = $true
    if ([string]::IsNullOrWhiteSpace($skin.Frames.Idle)) { throw "Missing idle frame: $($manifest.FullName)" }
    $frames = @($skin.Frames.Idle)
    if ($skin.Frames.Popup) { $frames += $skin.Frames.Popup }
    $dimensions = $null
    foreach ($frame in $frames | Select-Object -Unique) {
        if ($frame -cnotmatch '\A[a-zA-Z0-9][a-zA-Z0-9_.-]*\.png\z') { throw "Invalid PNG filename: $frame" }
        $imagePath = Join-Path $manifest.DirectoryName $frame
        $bytes = [IO.File]::ReadAllBytes($imagePath)
        if ($bytes.Length -lt 24 -or $bytes.Length -gt 8388608 -or [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or [Text.Encoding]::ASCII.GetString($bytes,12,4) -ne 'IHDR') { throw "Invalid PNG: $imagePath" }
        $width = [BitConverter]::ToUInt32([byte[]]@($bytes[19],$bytes[18],$bytes[17],$bytes[16]),0)
        $height = [BitConverter]::ToUInt32([byte[]]@($bytes[23],$bytes[22],$bytes[21],$bytes[20]),0)
        if ($width -lt 1 -or $width -gt 4096 -or $height -lt 1 -or $height -gt 4096) { throw "PNG dimensions exceed 1..4096: $imagePath" }
        $currentDimensions = "$width,$height"
        if ($null -ne $dimensions -and $dimensions -ne $currentDimensions) { throw "Skin frames have different canvas sizes: $imagePath" }
        $dimensions = $currentDimensions
    }
    $count++
}
if (-not $ids.ContainsKey('whale')) { throw 'The embedded default whale skin is required.' }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RootPath 'devices\profiles') -Filter '*.json' -File) {
    $profile = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($profile.Product) -or [string]::IsNullOrWhiteSpace($profile.Name) -or $profile.Vid -notmatch '\A[0-9a-fA-F]{4}\z' -or $profile.Pid -notmatch '\A[0-9a-fA-F]{4}\z') { throw "Invalid device profile: $($file.FullName)" }
    $count++
}
Write-Output "Validated $count skin and device manifests."
