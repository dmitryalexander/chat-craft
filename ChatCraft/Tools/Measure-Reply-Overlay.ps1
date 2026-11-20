$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Windows.Forms
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$app=Join-Path $root 'ChatCraft/Bleeding-Edge/App'
Add-Type @'
using System.Runtime.InteropServices;
public static class JapaneseFixtureNative {
 [DllImport("kernel32",CharSet=CharSet.Unicode)] public static extern bool SetDllDirectory(string path);
}
'@
[void][JapaneseFixtureNative]::SetDllDirectory($app)
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $app 'ChatCraft.exe'))
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
function Field($o,$name){$o.GetType().GetField($name,$flags).GetValue($o)}
function Prop($o,$name){$o.GetType().GetProperty($name,$flags).GetValue($o,$null)}
function Invoke($o,$name,[object[]]$values){$o.GetType().GetMethod($name,$flags).Invoke($o,$values)}
function Check($value,$message){if(!$value){throw $message}}
function Pump{[Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::Background)}
$client=[Activator]::CreateInstance($assembly.GetType('CodexCraftClient'),$flags,$null,[object[]]@($null,[string]$root),$null)
$window=[Activator]::CreateInstance($assembly.GetType('ChatCraftWindow'),$flags,$null,[object[]]@($client),$null)
Invoke (Field $window morse) Dispose
$grid=$window.Content;$window.Content=$null
$parameters=New-Object Windows.Interop.HwndSourceParameters 'Japanese rendering fixture'
$parameters.Width=1920;$parameters.Height=1080;$parameters.PositionX=-30000;$parameters.PositionY=-30000
$parameters.WindowStyle=-1879048192
$hwnd=New-Object Windows.Interop.HwndSource $parameters
$hwnd.RootVisual=$grid

try {
 $grid.UpdateLayout()
 $text=('The branching clouds drift while words move gently across the screen. '*150)
 Invoke $window OpenReplyText @($text)
 $flow=Field $window flow;$reader=Prop $flow Reader
 $reader.PausePlayback();(Field $window timer).Stop()
 $overlay=Field $window replyOverlay
 $grid.UpdateLayout()
 $times=New-Object 'Collections.Generic.List[double]'
 for($i=0;$i -lt 110;$i++) {
  [void]$reader.Step(1)
  $clock=[Diagnostics.Stopwatch]::StartNew()
  Invoke $overlay UpdateFrame
  $clock.Stop()
  if($i -ge 10){$times.Add($clock.Elapsed.TotalMilliseconds)}
 }
 $sorted=@($times|Sort-Object)
 $image=Field $overlay image
 [pscustomobject]@{Version=(Get-Item (Join-Path $app 'ChatCraft.exe')).VersionInfo.FileVersion;Width=$image.PixelWidth;Height=$image.PixelHeight;MeanMs=($times|Measure-Object -Average).Average;P95Ms=$sorted[94];Samples=$times.Count} | ConvertTo-Json
}finally{$hwnd.Dispose();$window.Close()}
