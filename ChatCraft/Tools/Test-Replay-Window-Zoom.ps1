$ErrorActionPreference='Stop'
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot;$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ZoomFixtureNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
$static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$type=$window.GetType()
function SetField($o,$name,$v){$o.GetType().GetField($name,$flags).SetValue($o,$v)}
function Space($mods,$repeat=$false){Invoke $window HandleSpaceShortcut @([Windows.Input.Key]::Space,[Enum]::Parse([Windows.Input.ModifierKeys],$mods),$repeat)}
try {
 $draft=Field $window draft
 $draft.Text='CAPS';$draft.CaretIndex=4
 Check (!(Space Shift)) 'Shift+Space was consumed in an empty chat'
 $draft.SelectedText=' ';$draft.AppendText('LOCK')
 Check ($draft.Text -eq 'CAPS LOCK') 'Uppercase word spacing lost'
 Invoke $window OpenReplyText @('First second third fourth fifth sixth seventh eighth ninth tenth.')
 $reader=Prop (Field $window flow) Reader
 $reader.PausePlayback();$reader.Seek(25)
 Check (!(Space Shift)) 'Shift+Space replayed instead of returning to typing'
 Check (!(Prop $window ReplyOpen)) 'Shift+Space leaked into the shared reader'
 Check ($draft.Text -eq 'CAPS LOCK') 'Resuming typing erased draft'
 $draft.CaretIndex=$draft.Text.Length;$draft.SelectedText=' '
 Check ($draft.Text -eq 'CAPS LOCK ') 'Shift-space typing lost its space'
 $savedDraft=$draft.Text
 Check (Space 'Control, Shift') 'Ctrl+Shift+Space did not handle replay'
 Check ((Prop $window ReplyOpen) -and $reader.TextAnchor -eq 0 -and $reader.PlaybackDirection -eq 1) 'Replay did not start playing from beginning'
 $reader.PausePlayback();$reader.Seek(25);$anchor=$reader.TextAnchor
 Check (Space 'Control, Shift' $true) 'Held replay shortcut escaped'
 Check ($reader.TextAnchor -eq $anchor) 'Held shortcut repeatedly restarted reply'
 Check ($draft.Text -eq $savedDraft) 'Replay changed unsent text'
 # Ctrl+Tab restarts from idle/end and while an existing reply is already open.
 $tabKey=[Windows.Forms.Keys]([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::Tab)
 $reader.PausePlayback();$reader.Seek(50);Invoke $window ShowDraft
 Check ([bool](Invoke $window HandleReplyKey @($tabKey,$false))) 'Ctrl+Tab was not handled from draft'
 Check ((Prop $window ReplyOpen) -and $reader.TextAnchor -eq 0 -and $reader.PlaybackDirection -eq 0) 'Ctrl+Tab did not open at the beginning paused'
 $reader.PausePlayback();$reader.Seek(30);$holdAnchor=$reader.TextAnchor
 [void](Invoke $window HandleReplyKey @($tabKey,$true))
 Check ($reader.TextAnchor -eq $holdAnchor) 'Held Ctrl+Tab repeatedly restarted playback'
 $reader.StartPlayback(1)
 [void](Invoke $window HandleReplyKey @($tabKey,$false))
 Check ((Prop $window ReplyOpen) -and $reader.TextAnchor -eq 0 -and $reader.PlaybackDirection -eq 0) 'Ctrl+Tab in playing reply did not reset and pause'
 Check (!([bool]$reader.AdvancePlayback(.25)) -and $reader.TextAnchor -eq 0) 'Ctrl+Tab reply advanced while paused'
 [void](Invoke $window HandleReplyKey @([Windows.Forms.Keys]::Space,$false))
 Check ($reader.PlaybackDirection -eq 1) 'Space did not start playback after Ctrl+Tab'
 Check ($draft.Text -eq $savedDraft) 'Ctrl+Tab changed unsent draft'
 $reader.PausePlayback()
 SetField $window thinking $true;$reader.Seek(30);$holdAnchor=$reader.TextAnchor
 [void](Invoke $window HandleReplyKey @($tabKey,$false))
 Check ($reader.TextAnchor -eq $holdAnchor -and $reader.PlaybackDirection -eq 0) 'Ctrl+Tab replayed an old reply while waiting'
 SetField $window thinking $false
 # The native outer window must remain fixed while the inner canvas changes.
 $next=$type.GetMethod('NextSimulationScale',$static)
 Check ($next.Invoke($null,@(1.0,-120)) -eq .95) 'Wheel back did not shrink simulation'
 Check ($next.Invoke($null,@(.25,120)) -eq .30) 'Wheel forward did not grow simulation'
 Check ($next.Invoke($null,@(.25,-10000)) -eq .25) 'Simulation minimum is not25%'
 Check ($next.Invoke($null,@(1.0,10000)) -eq 1) 'Simulation maximum exceeds viewport'
 Check ([Math]::Abs($next.Invoke($null,@(.5,30))-.5125) -lt 1e-9) 'High-resolution wheel increments lost'
 $helper=New-Object Windows.Interop.WindowInteropHelper $window
 $handle=$helper.EnsureHandle()
 Check (![ZoomFixtureNative]::IsWindowVisible($handle)) 'Fixture unexpectedly visible'
 $before=New-Object ZoomFixtureNative+Rect
 [void][ZoomFixtureNative]::GetWindowRect($handle,[ref]$before)
 $layout=Field $window layout
 $originalAnchor=$reader.TextAnchor
 foreach($scale in @(.25,.5,.95,1.0)) {
  Check ([bool](Invoke $window ApplySimulationScale @($scale))) 'Simulation scale rejected'
  $grid.UpdateLayout()
  Check ([Math]::Abs($layout.ActualWidth-$grid.ActualWidth*$scale) -lt 1) 'Simulation width incorrect'
  Check ([Math]::Abs($layout.ActualHeight-$grid.ActualHeight*$scale) -lt 1) 'Simulation height incorrect'
  $origin=$layout.TransformToAncestor($grid).Transform([Windows.Point]::new(0,0))
  Check ([Math]::Abs($origin.X-($grid.ActualWidth-$layout.ActualWidth)/2) -lt 1) 'Simulation not centered horizontally'
  Check ([Math]::Abs($origin.Y-($grid.ActualHeight-$layout.ActualHeight)/2) -lt 1) 'Simulation not centered vertically'
  if($scale -lt 1) {
   Check ($layout.Clip -is [Windows.Media.RectangleGeometry] -and $layout.Clip.RadiusX -eq 24 -and $layout.Clip.RadiusY -eq 24) 'Inset canvas corners not rounded'
   Check (!$layout.Clip.FillContains([Windows.Point]::new(1,1))) 'Rounded corner still contains square corner'
  } else {Check ($null -eq $layout.Clip) 'Full-size canvas retained rounded cutouts'}
  $actual=New-Object ZoomFixtureNative+Rect
  [void][ZoomFixtureNative]::GetWindowRect($handle,[ref]$actual)
  Check ($actual.Left -eq $before.Left -and $actual.Top -eq $before.Top -and $actual.Right -eq $before.Right -and $actual.Bottom -eq $before.Bottom) 'Wheel scaling resized or moved outer window'
  Check ($reader.TextAnchor -eq $originalAnchor -and $draft.Text -eq $savedDraft) 'Simulation scaling changed reply/draft'
 }
 [void](Invoke $window ApplySimulationScale @(.5));$grid.UpdateLayout()
 Check ([bool](Invoke $window InsideSimulation @([Windows.Point]::new(10,10)))) 'Inner pointer hit rejected'
 Check (![bool](Invoke $window InsideSimulation @([Windows.Point]::new(-10,10)))) 'Black matte started pointer gesture'
 Check (![bool](Invoke $window InsideSimulation @([Windows.Point]::new(1,1)))) 'Clipped corner started pointer gesture'
 $capture=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $capture.Render($grid)
 $pixels=[byte[]]::new($capture.PixelWidth*$capture.PixelHeight*4);$capture.CopyPixels($pixels,$capture.PixelWidth*4,0)
 foreach($point in @(@(2,2),@(($capture.PixelWidth-3),2),@(2,($capture.PixelHeight-3)),@(($capture.PixelWidth-3),($capture.PixelHeight-3)))) {
  $index=4*($point[1]*$capture.PixelWidth+$point[0])
  Check ($pixels[$index] -eq 0 -and $pixels[$index+1] -eq 0 -and $pixels[$index+2] -eq 0 -and $pixels[$index+3] -eq 255) 'Outer matte is not opaque black'
 }
 $origin=$layout.TransformToAncestor($grid).Transform([Windows.Point]::new(0,0))
 foreach($corner in @(@(2,2),@(($layout.ActualWidth-3),2),@(2,($layout.ActualHeight-3)),@(($layout.ActualWidth-3),($layout.ActualHeight-3)))) {
  $x=[int]($origin.X+$corner[0]);$y=[int]($origin.Y+$corner[1]);$index=4*($y*$capture.PixelWidth+$x)
  Check ($pixels[$index] -eq 0 -and $pixels[$index+1] -eq 0 -and $pixels[$index+2] -eq 0) 'Rounded canvas corner did not reveal black fill'
 }
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($capture))
 $file=[IO.File]::Create((Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/Rounded-Simulation-0.20.3.png'))
 try{$encoder.Save($file)}finally{$file.Dispose()}
 Invoke $window ToggleColors;$grid.UpdateLayout()
 $panel=Field $window colorPanel
 $testDir=Join-Path $root ('ChatCraft/Bleeding-Edge/Validation/Zoom-'+[guid]::NewGuid())
 New-Item -ItemType Directory -Path $testDir | Out-Null
 SetField (Field $panel profiles) FilePath (Join-Path $testDir 'Colors.json')
 SetField $window volumeSettingsPath (Join-Path $testDir 'Audio.json')
 Check ([Windows.Media.VisualTreeHelper]::GetParent($panel) -eq $grid) 'Settings shrank with simulation'
 Check ($panel.ActualWidth -eq 350) 'Settings width was zoomed'
 Invoke $window CloseColors
 Check (![ZoomFixtureNative]::IsWindowVisible($handle)) 'Scaling showed hidden test window'
 'PASS: Shift-space/replay/repeat guards;25/50/95/100% centered simulation only; outer HWND fixed; opaque black matte pixels; draft/reply preserved; pointer boundary; unscaled settings. No visible app/audio/chat.'
}finally{$hwnd.Dispose();$window.Close()}

