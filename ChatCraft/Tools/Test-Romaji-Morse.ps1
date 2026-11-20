$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe'))
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance'
$type=$assembly.GetType('JapaneseRomaji')
$romaji=[Activator]::CreateInstance($type,$true)
$method=$type.GetMethod('Romanize',$flags)
$at=$type.GetMethod('At',$flags)
$morseType=$assembly.GetType('DialUpMorse')
$morse=[Activator]::CreateInstance($morseType,$flags,$null,[object[]]@($false),$null)
$render=$morseType.GetMethod('Render',$flags)
try{
 $cases=@{
  'し'='SHI';'シム'='SHIMU';'しゃ'='SHA';'キャット'='KYATTO';'まっちゃ'='MATCHA';
  'がっこう'='GAKKOU';'コーヒー'='KOOHII';'ティー'='TII';'ヴァ'='VA';
  'ｶﾞｯﾂ'='GATTSU';"か$([char]0x3099)"='GA';'日本語'='NIHONGO';'abc'='ABC'
 }
 foreach($word in $cases.Keys){
  $actual=$method.Invoke($romaji,[object[]]@([string]$word))
  if($actual -ne $cases[$word]){throw "Romaji mismatch: $word -> $actual, expected $($cases[$word])"}
  $joined=''
  foreach($offset in [Globalization.StringInfo]::ParseCombiningCharacters($word)){
   $joined+=$at.Invoke($romaji,[object[]]@([string]$word,[int]$offset))
  }
  if($joined -ne $actual){throw "Focused playback repeated or lost romaji: $word -> $joined"}
  $japanese=$render.Invoke($morse,[object[]]@([string]$word,[double].25,[double].022,[double].010))
  $latin=$render.Invoke($morse,[object[]]@([string]$actual,[double].25,[double].022,[double].010))
  if($japanese.Length -ne $latin.Length){throw "Japanese/romaji Morse durations differ: $word"}
 }
 foreach($symbol in 'E','I','S','M','O','0','?'){
  foreach($pace in .015,.05,.2){
   $samples=$render.Invoke($morse,[object[]]@([string]$symbol,[double]$pace,[double].009,[double]0))
   $end=$samples.Length-1;while($end -ge 0 -and $samples[$end] -eq 0){$end--}
   if($end -lt 0 -or $samples.Length-$end-1 -lt 2880){throw 'Morse gap regressed'}
  }
 }
 $samples=$render.Invoke($morse,[object[]]@('し シム',[double].25,[double].022,[double].010))
 $path=Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/Romaji-SHI-SHIMU-0.18.20.wav'
 $writer=New-Object IO.BinaryWriter ([IO.File]::Create($path))
 try{
  $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int](36+$samples.Length*2))
  $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16)
  $writer.Write([int16]1);$writer.Write([int16]1);$writer.Write([int]24000);$writer.Write([int]48000)
  $writer.Write([int16]2);$writer.Write([int16]16)
  $writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]($samples.Length*2))
  foreach($sample in $samples){$writer.Write([int16]$sample)}
 }finally{$writer.Dispose()}
 "PASS: $($cases.Count) kana/kanji/Latin cases, contextual focused plans, Morse expansion and 21 spacing checks. No audio output device opened."
}finally{$morse.Dispose();$romaji.Dispose()}
