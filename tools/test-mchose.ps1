param([int]$WidgetProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\dist\widget-process.txt')))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-test.ps1') -WidgetProcessId $WidgetProcessId -SnapshotOnly | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$cursorBefore = [WidgetSmokeNative+Point]::new()
[WidgetSmokeNative]::GetCursorPos([ref]$cursorBefore) | Out-Null
try {
    $petHandle = [WidgetSmokeNative]::Find($WidgetProcessId,'小电量管家')
    if ($petHandle -eq [IntPtr]::Zero) { throw 'Widget not found.' }
    # Keep the pointer in the popup so another window taking focus cannot dismiss it during inspection.
    [WidgetSmokeNative]::SetCursorPos(1490,600) | Out-Null
    [WidgetSmokeNative]::SetForegroundWindow($petHandle) | Out-Null
    [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.ShowDevices.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 200
    $popupHandle = [WidgetSmokeNative]::Find($WidgetProcessId,'设备电量')
    if ($popupHandle -eq [IntPtr]::Zero) { throw 'Popup not visible.' }
    $popupRect = Get-WidgetRect $popupHandle
    [WidgetSmokeNative]::SetCursorPos(($popupRect.Left+140),($popupRect.Top+110)) | Out-Null
    [WidgetSmokeNative]::SetForegroundWindow($popupHandle) | Out-Null
    $root = [Windows.Automation.AutomationElement]::FromHandle($popupHandle)
    Wait-ForQuery $root
    $batteries = $root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'Battery_迈从 K5 Ultra'))
    if ($batteries.Count -ne 1) { throw 'K5 Ultra is missing or duplicated.' }
    $battery = $batteries[0]
    if ($battery.Current.Name -notmatch '迈从 K5 Ultra：(\d+)%' -or [int]$Matches[1] -gt 100 -or $battery.Current.HelpText -notlike 'MCHOSE 官方 HID 协议 | *') { throw 'K5 Ultra real reading missing from popup.' }
    $readingText = $battery.Current.Name
    $checks.Add('Popup shows one K5 Ultra with its real percentage and official protocol source')
    $helpBefore = $battery.Current.HelpText
    Start-Sleep -Milliseconds 2200
    if ((Get-ElementById $root 'Battery_迈从 K5 Ultra').Current.HelpText -ne $helpBefore) { throw 'K5 Ultra refreshed periodically.' }
    $checks.Add('The open popup does not periodically query K5 Ultra')
    $refresh = Get-ElementById $root 'RefreshStatusButton'
    ([Windows.Automation.InvokePattern]$refresh.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    # Invoke returns before WPF dispatches the click; the previous completion label can still be visible.
    $fresh = $false
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        Start-Sleep -Milliseconds 100
        $updated = Get-ElementById $root 'Battery_迈从 K5 Ultra'
        $status = Get-ElementById $root 'QueryStatus'
        if ($null -ne $updated -and $updated.Current.HelpText -ne $helpBefore -and $status.Current.Name.StartsWith('电量更新 ')) { $fresh = $true; break }
    }
    if (-not $fresh) {
        [WidgetSmokeNative]::Dump($WidgetProcessId)
        Write-Output ('Before: '+$helpBefore)
        Write-Output ('Status: '+$status.Current.Name+' / button enabled: '+$refresh.Current.IsEnabled)
        if ($null -ne $updated) { Write-Output ('After: '+$updated.Current.Name+' / '+$updated.Current.HelpText) }
        $currentCursor = [WidgetSmokeNative+Point]::new(); [WidgetSmokeNative]::GetCursorPos([ref]$currentCursor) | Out-Null
        Write-Output ('Cursor: '+$currentCursor.X+','+$currentCursor.Y)
        throw 'Manual refresh did not obtain a new K5 Ultra reading.'
    }
    $checks.Add('Manual refresh obtains a newly timestamped K5 Ultra reading')
    $popupRect = Get-WidgetRect $popupHandle
    $petRect = Get-WidgetRect $petHandle
    $left = [Math]::Min($popupRect.Left,$petRect.Left); $top = [Math]::Min($popupRect.Top,$petRect.Top)
    $right = [Math]::Max($popupRect.Right,$petRect.Right); $bottom = [Math]::Max($popupRect.Bottom,$petRect.Bottom)
    $bitmap = [Drawing.Bitmap]::new($right-$left,$bottom-$top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($left,$top,0,0,$bitmap.Size); $bitmap.Save((Join-Path $OutputDirectory 'actual-widget.png'),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $hide = Get-ElementById $root 'HidePetFromDialogButton'
    ([Windows.Automation.InvokePattern]$hide.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 150
    if ([WidgetSmokeNative]::IsWindowVisible($petHandle) -or [WidgetSmokeNative]::IsWindowVisible($popupHandle)) { throw 'Hide failed.' }
    $hiddenHash = Get-CpuCacheHash
    Start-Sleep -Milliseconds 2200
    if ((Get-CpuCacheHash) -ne $hiddenHash) { throw 'Hidden widget continued sampling.' }
    $checks.Add('Hiding the widget closes both windows and leaves collection idle')
    [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.RestorePet.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    if (-not [WidgetSmokeNative]::IsWindowVisible($petHandle) -or [WidgetSmokeNative]::IsWindowVisible($popupHandle)) { throw 'Restore failed.' }
    $checks.Add('Tray restore action restores only the character')
    @{ReadAt=[DateTime]::Now.ToString('o');Passed=$checks.Count;Reading=$readingText;Checks=@($checks)} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'mchose-ui-test.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $OutputDirectory 'mchose-ui-test.json') -Raw -Encoding UTF8
}
finally { [WidgetSmokeNative]::SetCursorPos($cursorBefore.X,$cursorBefore.Y) | Out-Null }
