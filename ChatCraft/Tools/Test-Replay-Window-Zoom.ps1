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
 Check ((Prop $window ReplyOpen) -and $reader.TextAnchor -eq 0) 'Replay did not start from beginning'
 $reader.PausePlayback();$reader.Seek(25);$anchor=$reader.TextAnchor
 Check (Space 'Control, Shift' $true) 'Held replay shortcut escaped'
 Check ($reader.TextAnchor -eq $anchor) 'Held shortcut repeatedly restarted reply'
 Check ($draft.Text -eq $savedDraft) 'Replay changed unsent text'
 # Test monitor geometry independently of the machine's actual layout.
 $next=$type.GetMethod('NextWindowScale',$static);$box=$type.GetMethod('ScaledWindowBounds',$static)
 Check ($next.Invoke($null,@(1.0,-120)) -eq .95) 'Wheel back did not shrink'
 Check ($next.Invoke($null,@(.25,120)) -eq .30) 'Wheel forward did not grow'
 Check ($next.Invoke($null,@(.25,-10000)) -eq .25) 'Minimum is not25%'
 Check ($next.Invoke($null,@(1.0,10000)) -eq 1) 'Maximum exceeds fullscreen'
 Check ([Math]::Abs($next.Invoke($null,@(.5,30))-.5125) -lt 1e-9) 'High-resolution wheel increments lost'
 $monitor=[Drawing.Rectangle]::new(-2560,-200,2560,1440)
 $rect=$box.Invoke($null,@($monitor,.25))
 Check ($rect.Width -eq 640 -and $rect.Height -eq 360 -and $rect.Left -eq -1600 -and $rect.Top -eq 340) 'Quarter-screen center/negative monitor coordinates wrong'
 # EnsureHandle creates a hidden owned window; never Show(), activate or play audio.
 $helper=New-Object Windows.Interop.WindowInteropHelper $window
 $handle=$helper.EnsureHandle()
 Check (![ZoomFixtureNative]::IsWindowVisible($handle)) 'Fixture unexpectedly visible'
 $screen=[Windows.Forms.Screen]::FromHandle($handle).Bounds
 foreach($scale in @(.25,.5,.95,1.0)) {
  Check ([bool](Invoke $window ApplyWindowScale @($scale))) 'Native resize failed'
  $actual=New-Object ZoomFixtureNative+Rect
  Check ([ZoomFixtureNative]::GetWindowRect($handle,[ref]$actual)) 'Native bounds read failed'
  $expected=$box.Invoke($null,@($screen,$scale))
  Check ($actual.Right-$actual.Left -eq $expected.Width -and $actual.Bottom-$actual.Top -eq $expected.Height) 'Actual native dimensions differ from requested scale'
  Check ($actual.Left -eq $expected.Left -and $actual.Top -eq $expected.Top) 'Actual native window is not centered'
 }
 Check (![ZoomFixtureNative]::IsWindowVisible($handle)) 'Resizing exposed hidden test window'
 [void](Invoke $window ApplyWindowScale @(.5));Invoke $window ToggleFullscreen
 Check ((Field $window windowScale) -eq 1) 'Fullscreen toggle failed'
 Invoke $window ToggleFullscreen
 Check ((Field $window windowScale) -eq .5) 'Fullscreen did not restore wheel size'
 Check ($draft.Text -eq $savedDraft) 'Window resizing changed draft'
 'PASS: Shift-space input/reply isolation, Ctrl+Shift+Space replay/repeat guards, preserved draft, wheel increments/clamps, centered negative-monitor geometry and actual hidden native25/50/95/100% resize/fullscreen restore. No chat messages/audio/visible app.'
}finally{$hwnd.Dispose();$window.Close()}

