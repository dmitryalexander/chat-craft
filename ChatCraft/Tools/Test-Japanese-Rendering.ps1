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
$parameters.Width=1280;$parameters.Height=720;$parameters.PositionX=-30000;$parameters.PositionY=-30000
$parameters.WindowStyle=-1879048192
$hwnd=New-Object Windows.Interop.HwndSource $parameters
$hwnd.RootVisual=$grid
function Draw($path){
 $grid.UpdateLayout()
 $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($grid)
 if($path){
  $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
  $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.File]::Create($path)
  try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
}
try{
 Pump;Draw
 $text='ふふふ シム 日本語 カタカナ ひらがな 漢字 abc'
 Invoke $window OpenReplyText @($text)
 $flow=Field $window flow;$reader=Prop $flow Reader
 $reader.PausePlayback();(Field $window timer).Stop()
 $name=$reader.FontFamilyName
 Check ($name -eq 'Yu Mincho' -or $name -eq 'Meiryo' -or $name -eq 'MS Mincho' -or $name -eq 'Yu Gothic') 'Reader did not select Japanese typography'
 $glyph=$null
 Check ((New-Object Windows.Media.Typeface $name).TryGetGlyphTypeface([ref]$glyph)) 'Could not inspect font'
 foreach($point in 0x3075,0x30B7,0x30E0,0x65E5,0x672C,0x8A9E){Check $glyph.CharacterToGlyphMap.ContainsKey($point) "Missing glyph U+$($point.ToString('X4'))"}
 Check ((Field $flow source).Text.Contains('日本語')) 'Source Japanese changed'
 for($i=0;$i -lt 6;$i++){[void]$reader.Step(1)}
 for($i=0;$i -lt 20;$i++){Start-Sleep -Milliseconds 20;Pump;Invoke (Field $window replyOverlay) UpdateFrame;Draw}
 $destination=Join-Path $root 'ChatCraft/Bleeding-Edge/Validation'
 Draw (Join-Path $destination 'Japanese-Flow-0.18.18.png')
 $scene=Field $window scene
 Invoke $window ShowDraft
 Invoke $scene ShowWord @('日本語',[bool]$true)
 Invoke $scene DepositTypedVersion @('','日本語','')
 Draw
 $shape=Invoke $scene BuildWord @('日本語')
 Check ($null -ne $shape -and !$shape.Bounds.IsEmpty -and $shape.Bounds.Width -gt 0) 'Japanese smoke geometry missing'
 for($i=0;$i -lt 6;$i++){Start-Sleep -Milliseconds 20;Pump;Draw}
 Draw (Join-Path $destination 'Japanese-Smoke-0.18.18.png')
 "PASS: $name covers kana/kanji, source preserved, Japanese Flow and smoke geometry rendered."
}finally{$hwnd.Dispose();$window.Close()}
