param([Parameter(Mandatory=$true)][int]$WidgetProcessId,[switch]$Administrator)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($Administrator) { throw 'Administrator confirmation failed.' }
    $helper = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-WidgetProcessId',$WidgetProcessId,'-Administrator') -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($helper.ExitCode -ne 0) { throw 'Could not close the elevated test widget.' }
    return
}
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist\BatteryPet.exe'))
$widget = Get-Process -Id $WidgetProcessId -ErrorAction Stop
if ($widget.Path -ne $executable) { throw 'The process does not belong to this project.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClosePetNative {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(IntPtr windowClass,string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
'@
$window = [ClosePetNative]::FindWindow([IntPtr]::Zero,([char[]](0x5C0F,0x7535,0x91CF,0x7BA1,0x5BB6) -join ''))
[uint32]$ownerId = 0
[ClosePetNative]::GetWindowThreadProcessId($window,[ref]$ownerId) | Out-Null
if ($ownerId -ne $widget.Id -or -not [ClosePetNative]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or -not $widget.WaitForExit(3000)) { throw 'The project widget did not close.' }
Write-Output ('Closed test widget '+$WidgetProcessId)
