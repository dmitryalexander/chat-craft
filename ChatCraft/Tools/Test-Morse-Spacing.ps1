$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe'))
$flags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
$type=$assembly.GetType('DialUpMorse')
$morse=[Activator]::CreateInstance($type,$flags,$null,[object[]]@($false),$null)
$render=$type.GetMethod('Render',$flags)
try{
 foreach($letter in 'E','I','S','M','O','0','?'){
  foreach($pace in .015,.05,.2){
   $samples=$render.Invoke($morse,[object[]]@([string]$letter,[double]$pace,[double].009,[double]0))
   $end=$samples.Length-1
   while($end -ge 0 -and $samples[$end] -eq 0){$end--}
   if($end -lt 0){throw "Silent Morse character: $letter"}
   $silence=$samples.Length-$end-1
   if($silence -lt 2880){throw "Short letter gap: $letter at $pace, $silence samples"}
  }
 }
 $audio=New-Object 'System.Collections.Generic.List[System.Int16]'
 foreach($letter in 'I','S','I','S','I','S'){
  $samples=$render.Invoke($morse,[object[]]@([string]$letter,[double].25,[double].022,[double].010))
  $audio.AddRange([int16[]]$samples)
 }
 $path=Join-Path $root 'ChatCraft/Bleeding-Edge/Validation/Morse-Letter-Gaps-0.18.19.wav'
 $writer=New-Object IO.BinaryWriter ([IO.File]::Create($path))
 try{
  $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int](36+$audio.Count*2))
  $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16)
  $writer.Write([int16]1);$writer.Write([int16]1);$writer.Write([int]24000);$writer.Write([int]48000)
  $writer.Write([int16]2);$writer.Write([int16]16)
  $writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]($audio.Count*2))
  foreach($sample in $audio){$writer.Write([int16]$sample)}
 }finally{$writer.Dispose()}
 'PASS: 21 fast/slow character renders retain at least120ms of final silence. Silent test; WAV demo contains I S repeated.'
}finally{$morse.Dispose()}
