$ErrorActionPreference='Stop'
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot;$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
function SetField($o,$name,$v){$o.GetType().GetField($name,$flags).SetValue($o,$v)}
function Descendants($o) {
 ,$o
 for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($o);$i++){Descendants ([Windows.Media.VisualTreeHelper]::GetChild($o,$i))}
}
try {
 $draft=Field $window draft;$draft.Text='preserve my draft'
 Invoke $window OpenReplyText @('The help overlay should pause this synthetic reply and preserve my place.')
 $reader=Prop (Field $window flow) Reader
 $reader.Seek(20);$reader.StartPlayback(1)
 Invoke $window ToggleHelp
 $help=Field $window helpOverlay
 Check ($null -ne $help) 'F1 did not open help'
 Check ($reader.PlaybackDirection -eq 0) 'Help did not pause reading'
 Check ([Windows.Media.VisualTreeHelper]::GetParent($help) -eq $grid) 'Help belongs to scaled simulation instead of full window'
 $help.BeginAnimation([Windows.UIElement]::OpacityProperty,$null);$help.Opacity=1
 $grid.UpdateLayout()
 $all=@(Descendants $help)
 $text=@($all | Where-Object {$_ -is [Windows.Controls.TextBlock]} | ForEach-Object {$_.Text})
 foreach($required in @('Keyboard controls','Ctrl','Shift','Space','Caps Lock','F1','Esc','Replay from the beginning','Simulation size · 25–100%')) {
  Check ($text -contains $required) ('Missing help label: '+$required)
 }
 $keycaps=@($all | Where-Object {$_ -is [Windows.Controls.Border] -and $_.CornerRadius.TopLeft -eq 7})
 Check ($keycaps.Count -ge 20) 'Keyboard caps were not drawn as individual rounded keys'
 foreach($cap in $keycaps){Check ($cap.ActualWidth -ge 40 -and $cap.ActualHeight -eq 43) 'A keyboard key collapsed'}
 $rows=@($all | Where-Object {$_ -is [Windows.Controls.Grid] -and $_.MinHeight -eq 44})
 Check ($rows.Count -ge 12) 'Missing compact shortcut rows'
 foreach($row in $rows) {
  $caps=$row.Children[0];$description=$row.Children[1]
  $capRight=$caps.TransformToAncestor($row).Transform([Windows.Point]::new($caps.ActualWidth,0)).X
  $textLeft=$description.TransformToAncestor($row).Transform([Windows.Point]::new(0,0)).X
  Check ([Math]::Abs(($textLeft-$capRight)-14) -lt 1) 'Shortcut description has a large fixed-column gap'
 }
 Check ($help.Background.Color.A -gt 0 -and $help.Background.Color.A -lt 255) 'Help backdrop is not translucent'
 [void](Invoke $window ApplySimulationScale @(.25));$grid.UpdateLayout()
 Check ([Math]::Abs($help.ActualWidth-$grid.ActualWidth) -lt 1) 'Help shrank with simulation'
 $capture=New-Object Windows.Media.Imaging.RenderTargetBitmap ([int]$grid.ActualWidth),([int]$grid.ActualHeight),96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $capture.Render($grid);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($capture))
 $file=[IO.File]::Create((Join-Path $root ('ChatCraft/Bleeding-Edge/Validation/F1-Keycap-Overlay-'+$assembly.GetName().Version.ToString(3)+'.png')))
 try{$encoder.Save($file)}finally{$file.Dispose()}
 $anchor=$reader.TextAnchor
 $displayTimer=Field $window timer
 $displayTimer.Stop() # Once the paused reply settles, its display timer sleeps.
 Invoke $window CloseHelp
 Invoke $window CloseHelp
 Check ((Field $window helpClosing)) 'Close did not arm fade'
 Invoke $window FinishHelpClose @($help)
 Check ($null -eq (Field $window helpOverlay)) 'Help did not dismiss'
 Check ($reader.PlaybackDirection -eq 1 -and $reader.TextAnchor -eq $anchor) 'Dismiss did not restore original playback position'
 Check ($displayTimer.IsEnabled) 'Help resumed reader but left white-text display timer asleep'
 $overlay=Field $window replyOverlay
 function FrameHash { $img=Field $overlay image;$bytes=[byte[]]::new($img.PixelWidth*$img.PixelHeight*4);$img.CopyPixels($bytes,$img.PixelWidth*4,0);$sha=[Security.Cryptography.SHA256]::Create();try{[Convert]::ToBase64String($sha.ComputeHash($bytes))}finally{$sha.Dispose()} }
 Invoke $window Tick
 $oldFrame=FrameHash
 Check ($reader.AdvancePlayback(.25)) 'Resumed reader did not advance'
 SetField $window nextFlowFrame 0.0
 Invoke $window Tick
 Check ($reader.TextAnchor -gt $anchor) 'Resumed reply position stayed frozen'
 Check ((FrameHash) -ne $oldFrame) 'White reply pixels stayed frozen after help dismissed'
 Check ($draft.Text -eq 'preserve my draft') 'Help changed unsent text'
 # A new final reply arriving while help is open must remain paused too.
 $reader.PausePlayback();Invoke $window ToggleHelp
 $help=Field $window helpOverlay
 Invoke $window OpenReplyText @('A new isolated reply that arrives behind the controls overlay.')
 Check ($reader.PlaybackDirection -eq 0) 'New reply started playing behind help'
 Invoke $window FinishHelpClose @($help)
 Check ($reader.PlaybackDirection -eq 1) 'New reply did not resume after help'
 $reader.PausePlayback();Invoke $window ToggleHelp
 Invoke $window FinishHelpClose @((Field $window helpOverlay))
 Check ($reader.PlaybackDirection -eq 0) 'Paused reply resumed without permission'
 'PASS: vector keycaps/translucent backdrop/full-window layout; reader pause/resume/arrival/paused-state; guarded close; preserved draft; screenshot captured. Offscreen, no audio/chat messages.'
}finally{$hwnd.Dispose();$window.Close()}
