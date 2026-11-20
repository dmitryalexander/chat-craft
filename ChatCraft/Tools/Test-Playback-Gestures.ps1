$ErrorActionPreference='Stop'
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot;$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
$testSettings=Join-Path $root ('ChatCraft/Bleeding-Edge/Validation/Gesture-Audio-'+[guid]::NewGuid()+'.json')
function SetField($o,$name,$value){$o.GetType().GetField($name,$flags).SetValue($o,$value)}
function SetProp($o,$name,$value){$o.GetType().GetProperty($name,$flags).SetValue($o,$value,$null)}
try {
 SetField $window volumeSettingsPath $testSettings
 $grid.UpdateLayout()
 $text=('A quiet cloud carries these words across a moving sky. '*80)+'the end'
 Invoke $window OpenReplyText @($text)
 $flow=Field $window flow;$reader=Prop $flow Reader;$morse=Field $window morse
 $reader.PausePlayback();(Field $window timer).Stop();$grid.UpdateLayout()
 $static=[Reflection.BindingFlags]'Public,NonPublic,Static'
 $type=$window.GetType()
 $volume=$type.GetMethod('DragVolume',$static);$anchor=$type.GetMethod('ScrubAnchor',$static)
 Check ($volume.Invoke($null,@(0.5,200.0,100.0)) -eq 1) 'Up is not louder'
 Check ($volume.Invoke($null,@(0.5,200.0,300.0)) -eq 0) 'Down is not quieter'
 Check ($anchor.Invoke($null,@(-10.0,100.0,101)) -eq 0) 'Left clamp failed'
 Check ($anchor.Invoke($null,@(50.0,100.0,101)) -eq 50) 'Midpoint mapping failed'
 Check ($anchor.Invoke($null,@(200.0,100.0,101)) -eq 100) 'Right clamp failed'

 $reader.Seek(30);$original=$reader.TextAnchor
 Invoke $window BeginReplyScrub
 Invoke $window SeekReplyScrub @(0.0)
 Check ($reader.TextAnchor -eq 0) 'Scrub start failed'
 Invoke $window SeekReplyScrub @($grid.ActualWidth)
 Check ($reader.TextAnchor -gt $text.Length*0.95) 'Scrub end failed'
 Invoke $window EndPlaybackGesture @($true,$true)
 Check ($reader.TextAnchor -eq $original) 'Escape did not restore anchor'
 Check ($reader.PlaybackDirection -eq 0) 'Paused reader resumed unexpectedly'

 $reader.Seek(0);$reader.StartPlayback(1)
 Invoke $window BeginReplyScrub
 Check ($reader.PlaybackDirection -eq 0) 'Drag did not pause playback'
 Invoke $window SeekReplyScrub @($grid.ActualWidth*0.5)
 $mid=$reader.TextAnchor
 Invoke $window EndPlaybackGesture @($false,$true)
 Check ($reader.PlaybackDirection -eq 1 -and $reader.TextAnchor -eq $mid) 'Playing reader did not resume at chosen point'
 $reader.PausePlayback()

 Invoke $window ShowDraft
 (Field $window draft).Text='preserve this unsent draft'
 Invoke $window BeginReplyScrub
 Invoke $window SeekReplyScrub @($grid.ActualWidth*0.2)
 Invoke $window EndPlaybackGesture @($true,$false)
 Check (!(Prop $window ReplyOpen)) 'Cancelled idle scrub left reply open'
 Check ((Field $window draft).Text -eq 'preserve this unsent draft') 'Scrub changed draft'
 Invoke $window BeginReplyScrub
 SetField $window pendingScrubX ([Nullable[double]]($grid.ActualWidth*0.7))
 Invoke $window TickReplyScrub
 Check ($null -eq (Field $window pendingScrubX)) 'Coalesced seek did not drain'
 Invoke $window EndPlaybackGesture @($false,$true)
 Check ((Prop $window ReplyOpen) -and $reader.PlaybackDirection -eq 0) 'Idle seek did not preserve paused state'

 SetProp $morse Volume 0.37;SetField $window volumeDirty $true
 Invoke $window SaveGestureVolume
 SetProp $morse Volume 1.0;Invoke $window LoadGestureVolume
 Check ([Math]::Abs((Prop $morse Volume)-0.37) -lt 0.0001) 'Isolated volume persistence failed'

 $ot=$assembly.GetType('MorseOutput')
 $output=[Activator]::CreateInstance($ot,$flags,$null,[object[]]@($false,$null),$null)
 try {
  SetProp $output Volume 0.5
  $samples=[int16[]]::new(480);for($i=0;$i -lt 480;$i++){$samples[$i]=12000}
  [void](Invoke $output Enqueue @(,$samples));$dest=[int16[]]::new(480)
  [void](Invoke $output Fill @(,$dest))
  Check ($dest[479] -eq 6000) 'Volume gain not applied to PCM'
  SetProp $output Volume 0.0;[void](Invoke $output Enqueue @(,$samples));[void](Invoke $output Fill @(,$dest))
  Check ($dest[479] -eq 0) 'Mute did not reach silence'
 }finally{$output.Dispose()}
 $layer=Field $window gestureLayer
 Invoke $layer Show @(0.7,'Reply 70%')
 $grid.UpdateLayout()
 $capture=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $capture.Render($grid)
 $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($capture))
 $file=[IO.File]::Create((Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/Playback-Gestures-0.18.27.png'))
 try{$encoder.Save($file)}finally{$file.Dispose()}
 'PASS: Nidhogg volume mapping/clamps, reply start/middle/end seeking, cancel/paused/playing/idle state, draft preservation, latest seek coalescing, isolated volume persistence and silent PCM gain/mute. No live chat messages.'
}finally{$hwnd.Dispose();$window.Close()}

