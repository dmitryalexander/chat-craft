$ErrorActionPreference='Stop'
$setup=Get-Content (Join-Path $PSScriptRoot 'Measure-Reply-Overlay.ps1') -Raw
$setup=$setup.Replace("`r`n","`n")
$setup=$setup.Substring(0,$setup.IndexOf("try {`n `$grid.UpdateLayout()"))
$fixtureRoot=$PSScriptRoot;$setup=$setup.Replace('$PSScriptRoot','$fixtureRoot')
Invoke-Expression $setup
$static=[Reflection.BindingFlags]'Static,Public,NonPublic'
$qualityType=$assembly.GetType('GpuQuality')
$qualityType.GetField('Remember',$static).SetValue($null,$false)
$testHome=Join-Path $root ('ChatCraft/Bleeding-Edge/Validation/Runtime-'+[guid]::NewGuid())
New-Item -ItemType Directory -Path $testHome | Out-Null
function SetField($o,$name,$v){$o.GetType().GetField($name,$flags).SetValue($o,$v)}
function SetProp($o,$name,$v){$o.GetType().GetProperty($name,$flags).SetValue($o,$v,$null)}
function Controls($element) {
 if($element -is [Windows.Controls.Slider] -or $element -is [Windows.Controls.ComboBox]){,$element}
 for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($element);$i++){Controls ([Windows.Media.VisualTreeHelper]::GetChild($element,$i))}
}
function FillNoise($n,$frames) {$buffer=[int16[]]::new($frames);[void](Invoke $n Fill @(,$buffer));,$buffer}
function Peak($a){$max=0;foreach($v in $a){$max=[Math]::Max($max,[Math]::Abs([int]$v))};$max}
try {
 SetField $window volumeSettingsPath (Join-Path $testHome 'Audio.json')
 $noise=Field $window backgroundNoise
 SetProp $noise PlaybackOnly $false
 SetProp $noise SecondaryWhileReading $true
 Invoke $window ToggleColors
 $panel=Field $window colorPanel
 $profiles=Field $panel profiles
 SetField $profiles FilePath (Join-Path $testHome 'Colors.json')
 $grid.UpdateLayout()
 $controls=@(Controls $panel)
 $master=$controls | Where-Object {[Windows.Automation.AutomationProperties]::GetName($_) -eq 'Sound volume'}
 $primary=$controls | Where-Object {[Windows.Automation.AutomationProperties]::GetName($_) -eq 'Primary background volume'}
 $secondary=$controls | Where-Object {[Windows.Automation.AutomationProperties]::GetName($_) -eq 'Secondary background sound'}
 $quality=$controls | Where-Object {[Windows.Automation.AutomationProperties]::GetName($_) -eq 'Graphics quality'}
 Check ($master -and $primary -and $secondary -and $quality) 'Missing real settings controls'
 $master.Value=37;$primary.Value=21;$secondary.SelectedIndex=2
 Check ([Math]::Abs((Prop (Field $window morse) Volume)-.37) -lt .001) 'Master slider did not update output'
 Check ([Math]::Abs((Prop $noise Level)-.21) -lt .001) 'Noise slider did not update generator'
 Check ((Prop $noise SecondaryKind) -eq 2) 'Noise choice did not update'
 $quality.Value=0
 Check ($qualityType.GetProperty('Tier',$static).GetValue($null,$null).ToString() -eq 'Low') 'Quality slider did not change tier'
 $quality.Value=3
 Check ($qualityType.GetProperty('Tier',$static).GetValue($null,$null).ToString() -eq 'Ultra') 'Quality slider did not reach Ultra'
 Check ([bool](Invoke $window SaveAudioValues)) 'Audio save failed'
 $saved=Get-Content (Join-Path $testHome 'Audio.json') -Raw | ConvertFrom-Json
 Check ($saved.volume -eq .37 -and $saved.noiseVolume -eq .21 -and $saved.secondaryNoiseKind -eq 2) 'Audio saved wrong values'
 Invoke $window SetMasterVolume @(0.8)
 Invoke $window LoadAudioValues
 Check ([Math]::Abs((Prop (Field $window morse) Volume)-.37) -lt .001) 'Audio restore failed'
 $gpuType=$assembly.GetType('GpuSumiInk');$tierType=$assembly.GetType('QualityTier');$gpu=$null
 try {
  foreach($q in 0..3) {
   $tier=[Enum]::ToObject($tierType,$q)
   if(!$gpu){$gpu=$gpuType.GetMethod('TryCreate',$static).Invoke($null,@([Windows.Size]::new(1280,720),$tier,$false,576))}
   else {[void](Invoke $gpu Resize @([Windows.Size]::new(1280,720),$tier))}
   Check ($null -ne $gpu -and !(Prop $gpu Failed)) 'Native quality allocation/resize failed'
   Check ((Field $gpu flowHeight) -eq @(144,288,432,576)[$q]) 'Quality did not reduce actual simulation rows'
   Check ((Field $gpu iterations) -eq @(12,16,22,32)[$q]) 'Quality did not reduce solver iterations'
  }
 } finally {if($gpu){$gpu.Dispose()}}
 Invoke $window OpenReplyText @('An isolated synthetic reply for sound gating.')
 $reader=Prop (Field $window flow) Reader
 Invoke $window CloseColors
 Invoke $window SyncBackgroundSound
 Check ((Prop $noise Playing)) 'Playback did not enable reading sound'
 $reader.PausePlayback();Invoke $window SyncBackgroundSound
 Check (!(Prop $noise Playing)) 'Pause did not disable reading sound'
 $reader.StartPlayback(1);SetField $window scrubbingReply $true;Invoke $window SyncBackgroundSound
 Check (!(Prop $noise Playing)) 'Scrub did not gate reading sound'
 SetField $window scrubbingReply $false;$window.WindowState=[Windows.WindowState]::Minimized
 Invoke $window SyncBackgroundSound
 Check ((Prop $noise Suspended)) 'Minimize did not mute background'
 $window.WindowState=[Windows.WindowState]::Normal
 $noiseType=$assembly.GetType('NoiseGenerator');$n=[Activator]::CreateInstance($noiseType,$true)
 Check ((Peak (FillNoise $n 4800)) -eq 0) 'Idle playback-only noise emitted'
 SetProp $n Playing $true
 Check ((Peak (FillNoise $n 12000)) -gt 0) 'Reading noise silent'
 SetProp $n Suspended $true;[void](FillNoise $n 144000)
 Check ((Peak (FillNoise $n 4800)) -eq 0) 'Minimize fade did not reach silence'
 $a=[Activator]::CreateInstance($noiseType,$true);$b=[Activator]::CreateInstance($noiseType,$true);$c=[Activator]::CreateInstance($noiseType,$true)
 foreach($item in @($a,$b,$c)){SetProp $item Playing $true;SetProp $item SecondaryWhileReading $true}
 SetProp $a SecondaryLevel 0.0;SetProp $b Level 0.0
 $aa=FillNoise $a 24000;$bb=FillNoise $b 24000;$cc=FillNoise $c 24000
 for($i=0;$i -lt $cc.Length;$i++){Check ([Math]::Abs([int]$aa[$i]+[int]$bb[$i]-[int]$cc[$i]) -le 1) 'Secondary noise replaced primary instead of adding'}
 $reader.PausePlayback();Invoke $window ToggleColors;$grid.UpdateLayout()
 $capture=New-Object Windows.Media.Imaging.RenderTargetBitmap 1920,1080,96,96,([Windows.Media.PixelFormats]::Pbgra32)
 $capture.Render($grid);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($capture))
 $file=[IO.File]::Create((Join-Path $testHome 'Settings-0.19.0.png'));try{$encoder.Save($file)}finally{$file.Dispose()}
 Invoke $window CloseColors
 "PASS: Caps controls, audio persistence, four native GPU resizes, playback/pause/scrub/minimize, additive PCM and silence. Evidence: $testHome"
}finally{$hwnd.Dispose();$window.Close()}
