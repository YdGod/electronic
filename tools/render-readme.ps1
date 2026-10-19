param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\outputs\github-preflight'),
    [string]$DiagnosticPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\images')
)
$ErrorActionPreference = 'Stop'
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $DiagnosticPath) { $DiagnosticPath = Join-Path $BuildDirectory 'devices.json' }
$DiagnosticPath = [IO.Path]::GetFullPath($DiagnosticPath)
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Runtime.Serialization
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BatteryPet.exe'))
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$snapshotType = $assembly.GetType('BatteryPet.ScanResult',$true)
$serializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($snapshotType)
$stream = [IO.File]::OpenRead($DiagnosticPath)
try { $snapshot = $serializer.ReadObject($stream) } finally { $stream.Dispose() }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function New-Control([string]$Name,[object[]]$Arguments = @()) {
    return [Activator]::CreateInstance($assembly.GetType('BatteryPet.' + $Name,$true),$flags,$null,$Arguments,[Globalization.CultureInfo]::InvariantCulture)
}
function Invoke-Control($Control,[string]$Name,[object[]]$Arguments) {
    return $Control.GetType().GetMethod($Name,$flags).Invoke($Control,$Arguments)
}
function Add-Element($Canvas,$Element,[double]$Left,[double]$Top) {
    [Windows.Controls.Canvas]::SetLeft($Element,$Left)
    [Windows.Controls.Canvas]::SetTop($Element,$Top)
    $Canvas.Children.Add($Element) | Out-Null
}
function Add-Label($Canvas,[string]$Text,[double]$Left,[double]$Top,[double]$Size = 14,[string]$Color = '#F4F5FC') {
    $label = [Windows.Controls.TextBlock]::new()
    $label.Text = $Text
    $label.FontFamily = [Windows.Media.FontFamily]::new('Microsoft YaHei UI, Segoe UI')
    $label.FontSize = $Size
    $label.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString($Color)
    Add-Element $Canvas $label $Left $Top
}
function New-Canvas([double]$Width,[double]$Height) {
    $canvas = [Windows.Controls.Canvas]::new()
    $canvas.Width = $Width
    $canvas.Height = $Height
    $canvas.Background = [Windows.Media.BrushConverter]::new().ConvertFromString('#242A39')
    return $canvas
}
function Save-Canvas($Canvas,[string]$Name) {
    $Canvas.Measure([Windows.Size]::new($Canvas.Width,$Canvas.Height))
    $Canvas.Arrange([Windows.Rect]::new(0,0,$Canvas.Width,$Canvas.Height))
    $Canvas.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]($Canvas.Width*2),[int]($Canvas.Height*2),192,192,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Canvas)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory $Name))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output ('Rendered: ' + (Join-Path $OutputDirectory $Name))
}

# Render the real WPF controls from a saved local snapshot without querying hardware.
$bubble = New-Control 'BubbleView'
Invoke-Control $bubble 'LimitHeight' @([double]1000) | Out-Null
Invoke-Control $bubble 'SetState' @($snapshot,$false,$null) | Out-Null
Invoke-Control $bubble 'SetTemperatures' @($snapshot.Temperatures) | Out-Null
$bubble.Measure([Windows.Size]::new(380,1000))
$height = [Math]::Max(720,[Math]::Ceiling($bubble.DesiredSize.Height) + 148)
$overview = New-Canvas 800 $height
Add-Label $overview '小电量管家 BatteryPet' 32 24 24
Add-Label $overview ('本机读取 · ' + $snapshot.ScannedAt.ToString('yyyy-MM-dd HH:mm') + ' · 数值仅代表截图时刻') 32 64 12 '#B6BDD1'
Add-Element $overview $bubble 32 108
$pet = New-Control 'PetView' @('whale')
Invoke-Control $pet 'SetPopupVisible' @($true,$false) | Out-Null
$pet.GetType().GetField('Caption',$flags).GetValue($pet).Text = $snapshot.Devices.Count.ToString() + ' 个设备 · 点击查看'
Add-Element $overview $pet 490 244
Save-Canvas $overview 'overview.png'

$states = New-Canvas 960 446
Add-Label $states '皮肤与弹窗状态' 32 24 24
Add-Label $states '同一套界面，两种皮肤；展开电量与温度弹窗时切换姿态。' 32 64 12 '#B6BDD1'
Add-Label $states '大肥鱼' 32 108 18
Add-Label $states '菲比啾比' 496 108 18
$index = 0
foreach ($id in @('whale','phoebe')) {
    foreach ($open in @($false,$true)) {
        $left = 32 + $index * 232
        Add-Label $states $(if ($open) { '弹窗展开' } else { '日常待机' }) $left 146 12 '#B6BDD1'
        $view = New-Control 'PetView' @($id)
        Invoke-Control $view 'SetPopupVisible' @($open,$false) | Out-Null
        Add-Element $states $view $left 180
        $index++
    }
}
Save-Canvas $states 'skin-states.png'
