param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\research\skin-check'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$executable = Join-Path $OutputDirectory 'BatteryPet.exe'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom($executable)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$checks = [Collections.Generic.List[string]]::new()
$settingsPath = Join-Path $env:LOCALAPPDATA 'BatteryPet\settings.json'
$savedSettings = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$window = $null
$app = [Windows.Application]::new()
$app.ShutdownMode = [Windows.ShutdownMode]::OnExplicitShutdown
function Require([bool]$Condition,[string]$Name) {
    if (-not $Condition) {
        if ($null -ne $pet) {
            @{Failed=$Name;Checks=@($checks);Skin=(Read-Property $pet 'SkinId');Saluting=(Read-Property $pet 'IsSaluting');PopupVisible=$popup.IsVisible;PetVisible=$window.IsVisible;PetLoaded=$pet.IsLoaded;Opacity=$current.Opacity;PreviousPresent=($null -ne $previous.Source);AnimationEnabled=[Windows.SystemParameters]::ClientAreaAnimation} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'skin-ui-failure.json') -Encoding UTF8
        }
        throw $Name
    }
    $checks.Add($Name)
}
function Read-Field($Object,[string]$Name) { $Object.GetType().GetField($Name,$flags).GetValue($Object) }
function Read-Property($Object,[string]$Name) { $Object.GetType().GetProperty($Name,$flags).GetValue($Object,$null) }
function Pump([int]$Milliseconds) {
    $frame = [Windows.Threading.DispatcherFrame]::new()
    $timer = [Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds($Milliseconds)
    $tick = { $timer.Stop(); $frame.Continue = $false }.GetNewClosure()
    $timer.Add_Tick($tick)
    $timer.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
    $timer.Remove_Tick($tick)
}
function Save-Sprite([string]$Name) {
    $sprite.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(610,610,610.0/210*96,610.0/210*96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($sprite)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory $Name))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
function Wait-Blend {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if ($current.Opacity -eq 1 -and $null -eq $previous.Source) { return }
        Pump 50
    }
}
function Click-SkinItem($Item) {
    $Item.GetType().GetMethod('OnClick',$flags).Invoke($Item,[object[]]@()) | Out-Null
}
try {
    $type = $assembly.GetType('BatteryPet.PetWindow',$true)
    $window = [Activator]::CreateInstance($type,$flags,$null,[object[]]@($false),[Globalization.CultureInfo]::InvariantCulture)
    $window.Show()
    Pump 100
    $pet = Read-Field $window 'pet'
    $popup = Read-Field $window 'popup'
    $bubble = Read-Field $window 'bubble'
    $sprite = Read-Field $pet 'sprite'
    $current = Read-Field $pet 'current'
    $previous = Read-Field $pet 'previous'
    $skins = $window.ContextMenu.Items[0]
    Require ($skins.Header -eq '小人皮肤' -and $window.ContextMenu.Items[1].Header -eq '查看电量与温度') 'Skin menu appears above the popup option'
    Require ($skins.Items.Count -eq 2 -and $skins.Items[0].Header -eq '大肥鱼' -and $skins.Items[1].Header -eq '菲比啾比') 'Both requested skin names appear in the real menu'
    $skins.Items[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.MenuItem]::ClickEvent))
    Pump 260
    Require ((Read-Property $pet 'SkinId') -eq 'phoebe' -and -not (Read-Property $pet 'IsSaluting')) 'Selecting Phoebe from the menu displays idle'
    Require ($skins.Items[1].IsChecked -and -not $skins.Items[0].IsChecked) 'Skin menu selection is exclusive'
    $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Require ($saved.Skin -eq 'phoebe') 'Real menu selection saves the skin to settings'
    $bounds = @($window.Width,$window.Height,$window.Left,$window.Top)
    Save-Sprite 'phoebe-idle-render.png'
    $popup.Show()
    Pump 60
    Require (Read-Property $pet 'IsSaluting') 'Popup visibility immediately sets the salute state'
    if ([Windows.SystemParameters]::ClientAreaAnimation) {
        Require ($current.Opacity -gt 0 -and $current.Opacity -lt 1 -and $null -ne $previous.Source) 'A real WPF animation blends the registered frames'
    }
    Save-Sprite 'phoebe-transition-render.png'
    Pump 260
    Save-Sprite 'phoebe-salute-render.png'
    Require ($current.Opacity -eq 1 -and $null -eq $previous.Source) 'Completed animation releases the outgoing frame'
    Pump 650
    Require (Read-Property $pet 'IsSaluting') 'Salute remains active while the popup stays open'
    Require ($window.Width -eq $bounds[0] -and $window.Height -eq $bounds[1] -and $window.Left -eq $bounds[2] -and $window.Top -eq $bounds[3]) 'Pose changes keep the exact pet window size and position'
    $skinButton = Read-Field $bubble 'skinButton'
    $titleSkins = Read-Field $bubble 'skinMenu'
    $header = $skinButton.Parent
    Require ($skinButton.Content -eq '切换皮肤' -and [Windows.Controls.Grid]::GetColumn($skinButton) -eq 1 -and $header.Children[0].Text -eq '电量与温度' -and [Windows.Controls.Grid]::GetColumn($header.Children[2]) -eq 2) 'The new skin button sits between the popup title and close button'
    $titleEnd = $header.Children[0].TranslatePoint([Windows.Point]::new($header.Children[0].ActualWidth,0),$header).X
    $buttonStart = $skinButton.TranslatePoint([Windows.Point]::new(0,0),$header).X
    $buttonEnd = $skinButton.TranslatePoint([Windows.Point]::new($skinButton.ActualWidth,0),$header).X
    $closeStart = $header.Children[2].TranslatePoint([Windows.Point]::new(0,0),$header).X
    Require ($titleEnd -le $buttonStart -and $buttonEnd -le $closeStart) 'Popup title, skin button and close button fit without overlap'
    Require ($titleSkins.Items[1].IsChecked -and -not $titleSkins.Items[0].IsChecked) 'Popup skin choices reflect the selection made in the right-click menu'
    $skinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)); Pump 100
    Require ($titleSkins.IsOpen -and $popup.IsVisible -and (Read-Property $pet 'IsSaluting')) 'Opening the skin choices keeps the popup and salute visible'
    $menuImage = [Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($titleSkins.ActualWidth*2),[int][Math]::Ceiling($titleSkins.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32)
    $menuImage.Render($titleSkins)
    $menuEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $menuEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($menuImage))
    $menuStream = [IO.File]::Create((Join-Path $OutputDirectory 'skin-picker-preview.png'))
    try { $menuEncoder.Save($menuStream) } finally { $menuStream.Dispose() }
    Click-SkinItem $titleSkins.Items[0]; Pump 260; Wait-Blend
    Require ($popup.IsVisible -and -not $titleSkins.IsOpen -and (Read-Property $pet 'SkinId') -eq 'whale' -and $skins.Items[0].IsChecked -and $titleSkins.Items[0].IsChecked) 'Choosing whale from the popup keeps it open and synchronizes both menus'
    $skinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)); Pump 100
    Click-SkinItem $titleSkins.Items[1]; Pump 260; Wait-Blend
    Require ($popup.IsVisible -and (Read-Property $pet 'IsSaluting') -and $skins.Items[1].IsChecked -and $titleSkins.Items[1].IsChecked) 'Choosing Phoebe from the popup displays salute and synchronizes both menus'
    $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Require ($saved.Skin -eq 'phoebe') 'Popup skin selection is saved for the next launch'
    $skinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)); Pump 80
    Click-SkinItem $titleSkins.Items[1]; Pump 150
    Require ($titleSkins.Items[1].IsChecked -and -not $titleSkins.Items[0].IsChecked -and $popup.IsVisible) 'Selecting the current skin retains one checked choice and keeps the popup open'
    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new(760,[int][Math]::Ceiling($bubble.ActualHeight*2),192,192,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($bubble)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory 'popup-skin-button.png'))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $popup.Hide(); Pump 50
    $popup.Show(); Pump 40
    $popup.Hide(); Pump 260
    Wait-Blend
    Require (-not (Read-Property $pet 'IsSaluting') -and $current.Opacity -eq 1 -and $null -eq $previous.Source) 'Rapid popup open and close completes cleanly in idle'
    $popup.Show(); Pump 260
    $skins.Items[0].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.MenuItem]::ClickEvent))
    Pump 260
    Require ((Read-Property $pet 'SkinId') -eq 'whale' -and -not (Read-Property $pet 'IsSaluting')) 'Skin can switch to whale while the popup is visible'
    $skins.Items[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.MenuItem]::ClickEvent))
    Pump 260
    Require (Read-Property $pet 'IsSaluting') 'Switching back with the popup visible uses salute'
    $skinButton.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent)); Pump 80
    (Read-Field $bubble 'CloseRequested').Invoke(); Pump 260
    Require (-not $popup.IsVisible -and -not $titleSkins.IsOpen -and -not (Read-Property $pet 'IsSaluting')) 'The popup close button dismisses skin choices and restores idle'
    $popup.Show(); Pump 260
    (Read-Field $pet 'HideRequested').Invoke(); Pump 100
    Require (-not $window.IsVisible -and -not $popup.IsVisible -and -not (Read-Property $pet 'IsSaluting')) 'Hiding the pet hides its popup and resets the pose'
    $type.GetMethod('BringPet',$flags).Invoke($window,@()) | Out-Null
    Pump 150
    Require ($window.IsVisible -and -not $popup.IsVisible -and -not (Read-Property $pet 'IsSaluting')) 'Restoring the pet returns to idle without opening the popup'
    @{Passed=$checks.Count;Checks=@($checks);Failures=@()} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'skin-ui-test.json') -Encoding UTF8
    Write-Output ('Skin UI checks passed: ' + $checks.Count)
}
finally {
    if ($null -ne $window) { $window.Close() }
    if ($null -ne $savedSettings) { [IO.File]::WriteAllBytes($settingsPath,$savedSettings) }
    elseif (Test-Path -LiteralPath $settingsPath) { [IO.File]::Delete($settingsPath) }
}
