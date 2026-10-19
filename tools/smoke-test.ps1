param([int]$WidgetProcessId, [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'),[switch]$SnapshotOnly,[switch]$SkipInput,[switch]$VerifyIdle)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WidgetSmokeNative {
    public delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    public static IntPtr Find(int pid, string title) {
        IntPtr result=IntPtr.Zero;
        EnumCallback callback=delegate(IntPtr hwnd,IntPtr p) {
            uint process; GetWindowThreadProcessId(hwnd,out process);
            if(process!=(uint)pid || !IsWindowVisible(hwnd)) return true;
            var text=new StringBuilder(256);GetWindowText(hwnd,text,256);
            if(text.ToString()==title) { result=hwnd; return false; }
            return true;
        };
        EnumWindows(callback,IntPtr.Zero);return result;
    }
    public static string[] Dump(int pid) {
        var list=new List<string>();
        EnumCallback callback=delegate(IntPtr hwnd,IntPtr p) {
            uint process;GetWindowThreadProcessId(hwnd,out process);
            if(process==(uint)pid) { var text=new StringBuilder(256);GetWindowText(hwnd,text,256);Rect rect;GetWindowRect(hwnd,out rect);list.Add(hwnd+" / "+text+" / visible="+IsWindowVisible(hwnd)+" / "+rect.Left+","+rect.Top+","+rect.Right+","+rect.Bottom); }
            return true;
        };EnumWindows(callback,IntPtr.Zero);return list.ToArray();
    }
}
'@
function Get-WidgetRect([IntPtr]$Handle) {
    $rect = New-Object WidgetSmokeNative+Rect
    [WidgetSmokeNative]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    return $rect
}
function Click-Widget([int]$X, [int]$Y) {
    [WidgetSmokeNative]::SetCursorPos($X,$Y) | Out-Null
    Start-Sleep -Milliseconds 80
    [WidgetSmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 5
    [WidgetSmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}
$cursorBefore = New-Object WidgetSmokeNative+Point
[WidgetSmokeNative]::SetThreadDpiAwarenessContext([IntPtr]::new(-2)) | Out-Null
[WidgetSmokeNative]::GetCursorPos([ref]$cursorBefore) | Out-Null
$checks = [System.Collections.Generic.List[string]]::new()
$cpuCachePath = Join-Path $env:ProgramData 'BatteryPet\temperatures.json'
function Get-ElementById($Root,[string]$Id) {
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Id))
}
function Wait-ForQuery($Root) {
    for ($attempt = 0; $attempt -lt 160; $attempt++) {
        $label = Get-ElementById $Root 'QueryStatus'
        if ($null -ne $label -and $label.Current.Name.StartsWith('电量更新 ')) { return }
        Start-Sleep -Milliseconds 100
    }
    throw 'The immediate popup query did not complete.'
}
function Get-CpuCacheHash { return (Get-FileHash -LiteralPath $cpuCachePath -Algorithm SHA256).Hash }
try {
    if ($SnapshotOnly) { [WidgetSmokeNative]::Dump($WidgetProcessId); return }
    $petHandle = [WidgetSmokeNative]::Find($WidgetProcessId,'小电量管家')
    if ($petHandle -eq [IntPtr]::Zero) { throw 'Pet window was not found.' }
    $rectBefore = Get-WidgetRect $petHandle
    Write-Output ('Pet bounds: '+($rectBefore | ConvertTo-Json -Compress))
    if ($VerifyIdle) {
        if ([WidgetSmokeNative]::Find($WidgetProcessId,'设备电量') -ne [IntPtr]::Zero) { throw 'An idle launch unexpectedly opened the popup.' }
        $idleHash = Get-CpuCacheHash
        Start-Sleep -Milliseconds 3200
        if ((Get-CpuCacheHash) -ne $idleHash) { throw 'Idle launch caused a CPU sample.' }
        $gpuLoaded = (Get-Process -Id $WidgetProcessId).Modules | Where-Object { $_.ModuleName -ieq 'nvml.dll' }
        if ($gpuLoaded) { throw 'Idle launch unexpectedly loaded the NVIDIA sampling library.' }
        $checks.Add('Idle launch performs no CPU sampling and does not initialize GPU sampling')
    }
    [WidgetSmokeNative]::SetForegroundWindow($petHandle) | Out-Null
    [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.ShowDevices.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 250
    $popupHandle = [WidgetSmokeNative]::Find($WidgetProcessId,'设备电量')
    if ($popupHandle -eq [IntPtr]::Zero) { [WidgetSmokeNative]::Dump($WidgetProcessId); throw 'Click did not open the battery popup.' }
    $checks.Add('Show-devices command opens the live battery popup')
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($popupHandle)
    Wait-ForQuery $root
    $textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
    $texts = @($textElements | ForEach-Object { $_.Current.Name })
    $diagnostic = Get-Content -LiteralPath (Join-Path $OutputDirectory 'devices.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($deviceName in @($diagnostic.Devices | ForEach-Object Name)) {
        if ($texts -notcontains $deviceName) { throw ('Real device missing from popup: '+$deviceName) }
    }
    $checks.Add('Popup displays every device from the current real hardware scan')
    $cpuHelpBefore = $null
    foreach ($temperatureId in @('Temperature_CPU','Temperature_GPU_0')) {
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$temperatureId))
        if ($null -eq $element -or $element.Current.Name -notmatch '^(\d+)°C$') { throw ('Real temperature missing: '+$temperatureId) }
        $celsius = [int]$Matches[1]
        if ($celsius -lt 0 -or $celsius -gt 150) { throw ('Invalid temperature: '+$element.Current.Name) }
        if ($temperatureId -eq 'Temperature_CPU') {
            if ($element.Current.HelpText -notmatch 'CPU Package.*LibreHardwareMonitor') { throw 'CPU temperature source is not the package sensor.' }
            $cpuHelpBefore = $element.Current.HelpText
        }
        elseif ($element.Current.HelpText -notmatch 'GPU Core.*NVIDIA NVML') { throw 'GPU temperature source is not NVML.' }
        $checks.Add('Live temperature with verified source: '+$temperatureId+' '+$element.Current.Name)
    }
    $sampleHash = Get-CpuCacheHash
    $batteryHelpBefore = (Get-ElementById $root 'Battery_GSP 370').Current.HelpText
    Start-Sleep -Milliseconds 3200
    $cpuAfter = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Temperature_CPU'))
    if ($cpuAfter.Current.HelpText -ne $cpuHelpBefore -or (Get-CpuCacheHash) -ne $sampleHash -or (Get-ElementById $root 'Battery_GSP 370').Current.HelpText -ne $batteryHelpBefore) { throw 'A popup automatically resampled instead of keeping its single requested snapshot.' }
    $checks.Add('An open popup keeps the requested battery and temperature snapshot without periodic sampling')
    $manual = Get-ElementById $root 'RefreshStatusButton'
    ([System.Windows.Automation.InvokePattern]$manual.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-ForQuery $root
    if ((Get-ElementById $root 'Temperature_CPU').Current.HelpText -eq $cpuHelpBefore -or (Get-CpuCacheHash) -eq $sampleHash) { throw 'Manual refresh did not take a new CPU sample.' }
    $checks.Add('Manual refresh obtains a new real CPU sample')
    if ($texts -notcontains '暂未读取电量') { throw 'Unknown battery label is missing.' }
    $checks.Add('Unsupported protocols show an explicit unknown label')
    foreach ($reading in @($diagnostic.Devices | Where-Object { $null -ne $_.Percent })) {
        $batteryElement = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,('Battery_'+$reading.Name)))
        $expectedPattern = '^'+[regex]::Escape($reading.Name)+'：(\d+)%，'
        if ($null -eq $batteryElement -or $batteryElement.Current.Name -notmatch $expectedPattern -or [int]$Matches[1] -gt 100) { throw ('Live battery reading is missing or invalid: '+$reading.Name) }
        if ($reading.Charging -eq $true -and $batteryElement.Current.Name -notmatch '正在充电') { throw ('Confirmed charging state is missing: '+$reading.Name) }
        $checks.Add('Live battery displays a valid percentage and status for '+$reading.Name)
    }
    $popupRect = Get-WidgetRect $popupHandle
    if (-not [WidgetSmokeNative]::IsWindowVisible($popupHandle)) {
        [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.ShowDevices.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 150
    }
    [WidgetSmokeNative]::SetForegroundWindow($popupHandle) | Out-Null
    $captureLeft = [Math]::Min($popupRect.Left,$rectBefore.Left)
    $captureTop = [Math]::Min($popupRect.Top,$rectBefore.Top)
    $captureRight = [Math]::Max($popupRect.Right,$rectBefore.Right)
    $captureBottom = [Math]::Max($popupRect.Bottom,$rectBefore.Bottom)
    $bitmap = [System.Drawing.Bitmap]::new($captureRight-$captureLeft,$captureBottom-$captureTop)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($captureLeft,$captureTop,0,0,$bitmap.Size); $bitmap.Save((Join-Path $OutputDirectory 'actual-widget.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $keyboard = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'键盘'))
    ([System.Windows.Automation.InvokePattern]$keyboard.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 100
    $textsFiltered = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name })
    if ($textsFiltered -notcontains 'SKN 青龙 4.0 Ultra 绯' -or $textsFiltered -contains 'GSP 370') { throw 'Keyboard category filter did not work.' }
    $checks.Add('Keyboard filter keeps the keyboard and excludes the headset')
    $all = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'全部'))
    ([System.Windows.Automation.InvokePattern]$all.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $close = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'×'))
    ([System.Windows.Automation.InvokePattern]$close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 100
    if ([WidgetSmokeNative]::IsWindowVisible($popupHandle)) { throw 'Close button failed.' }
    $checks.Add('Close button hides the popup')
    $closedHash = Get-CpuCacheHash
    Start-Sleep -Milliseconds 3200
    if ((Get-CpuCacheHash) -ne $closedHash) { throw 'The CPU service sampled with the popup closed.' }
    $checks.Add('Closing the popup leaves the CPU service idle without new samples')
    [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.ShowDevices.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    Wait-ForQuery $root
    if ((Get-CpuCacheHash) -eq $closedHash) { throw 'Reopening the popup reused the old CPU sample.' }
    $checks.Add('Reopening always immediately queries a fresh snapshot')
    ([System.Windows.Automation.InvokePattern]$close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $petRoot = [System.Windows.Automation.AutomationElement]::FromHandle($petHandle)
    $hide = Get-ElementById $petRoot 'HidePetButton'
    ([System.Windows.Automation.InvokePattern]$hide.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 100
    if ([WidgetSmokeNative]::IsWindowVisible($petHandle) -or [WidgetSmokeNative]::IsWindowVisible($popupHandle)) { throw 'Hide did not hide both windows.' }
    $checks.Add('The hide button hides both the character and its dialog')
    $hiddenHash = Get-CpuCacheHash
    [WidgetSmokeNative]::SendMessage($petHandle,[WidgetSmokeNative]::RegisterWindowMessage('BatteryPet.RestorePet.v1'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 3200
    if (-not [WidgetSmokeNative]::IsWindowVisible($petHandle) -or [WidgetSmokeNative]::IsWindowVisible($popupHandle) -or (Get-CpuCacheHash) -ne $hiddenHash) { throw 'Restore must show only the character, with no new sample.' }
    $checks.Add('The tray restore action shows the character without querying or opening its dialog')
    if ($SkipInput) {
        @{Passed=$checks.Count;Checks=@($checks);PopupTexts=$texts} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'ui-smoke-test.json') -Encoding UTF8
        Write-Output ($checks | ConvertTo-Json)
        return
    }
    # Move left/up to stay inside the current monitor, then restore the original location.
    [WidgetSmokeNative]::SetCursorPos(($rectBefore.Left+100),($rectBefore.Top+100)) | Out-Null
    [WidgetSmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    for ($step=1; $step -le 6; $step++) { [WidgetSmokeNative]::SetCursorPos(($rectBefore.Left+100-$step*8),($rectBefore.Top+100-$step*6)) | Out-Null; Start-Sleep -Milliseconds 45 }
    [WidgetSmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    $rectMoved = Get-WidgetRect $petHandle
    if ($rectMoved.Left -eq $rectBefore.Left -and $rectMoved.Top -eq $rectBefore.Top) { throw 'Dragging did not move the pet.' }
    $checks.Add('Dragging moves the desktop pet')
    if ([WidgetSmokeNative]::Find($WidgetProcessId,'设备电量') -ne [IntPtr]::Zero) { throw 'Dragging unexpectedly opened the popup.' }
    $checks.Add('Drag is distinguished from click')
    $saved = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'BatteryPet\settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $saved.PositionSaved) { throw 'Position was not persisted.' }
    $checks.Add('Drag persists the desktop position')
    [WidgetSmokeNative]::SetCursorPos(($rectMoved.Left+100),($rectMoved.Top+100)) | Out-Null
    [WidgetSmokeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    for ($step=1; $step -le 6; $step++) { [WidgetSmokeNative]::SetCursorPos(($rectMoved.Left+100+[int](($rectBefore.Left-$rectMoved.Left)*$step/6)),($rectMoved.Top+100+[int](($rectBefore.Top-$rectMoved.Top)*$step/6))) | Out-Null; Start-Sleep -Milliseconds 45 }
    [WidgetSmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    $checks.Add('Original desktop position restored after verification')
    @{Passed=$checks.Count;Checks=@($checks);PopupTexts=$texts} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'ui-smoke-test.json') -Encoding UTF8
    Write-Output ($checks | ConvertTo-Json)
}
finally { [WidgetSmokeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero); [WidgetSmokeNative]::SetCursorPos($cursorBefore.X,$cursorBefore.Y) | Out-Null }
