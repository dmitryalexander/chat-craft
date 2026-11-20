# Stage ChatCraft-only audio/quality adaptations; shared WordCraft sources stay unchanged.
$source=[IO.File]::ReadAllText((Join-Path $app 'DialUpMorse.cs')).Replace([string][char]13,'')
foreach($pair in @(
 @('internal MorseOutput(bool enableOutput = true)','internal MorseOutput(bool enableOutput = true, Action<short[]> generator = null)'),
 @('readonly Thread pump;','readonly Thread pump; readonly Action<short[]> generator;'),
 @('if (!enableOutput) return;','this.generator=generator; if (!enableOutput) return;'),
 @('int Fill(short[] destination)','int Fill(short[] destination)')
)) {
 if(!$source.Contains($pair[0])){throw 'Shared Morse transport changed; review staging.'}
 $source=$source.Replace($pair[0],$pair[1])
}
$needle="int Fill(short[] destination)"+[char]10+"    {"
if(!$source.Contains($needle)){throw 'Missing background audio generator hook.'}
$source=$source.Replace($needle,$needle+[char]10+'        if(generator!=null){generator(destination);return destination.Length;}')
$morseSource=Join-Path $stage.FullName 'DialUpMorse.cs'
[IO.File]::WriteAllText($morseSource,$source)
foreach($name in @('GpuSumiInk.cs','GpuPresenter.cs')) {
 $source=[IO.File]::ReadAllText((Join-Path $shared $name))
 if($name -eq 'GpuSumiInk.cs') {
  $needle='flowHeight = simulationRows;'
  if(!$source.Contains($needle)){throw 'Missing simulation quality hook.'}
  $source=$source.Replace($needle,'flowHeight = simulationRows == 576 ? new[] {144,288,432,576}[(int)tier] : simulationRows;')
  $source=$source.Replace('Math.Round(simulationRows * aspect)','Math.Round(flowHeight * aspect)')
 } else {
  $needle='int chosen = (int)Math.Round(ViewTuning.Get(prefix + "iterations"));'
  if(!$source.Contains($needle)){throw 'Missing solver quality hook.'}
  $source=$source.Replace($needle,'int chosen = prefix == "smoke." ? 0 : (int)Math.Round(ViewTuning.Get(prefix + "iterations"));')
 }
 $destination=Join-Path $stage.FullName $name
 [IO.File]::WriteAllText($destination,$source)
 $sources=@($sources | Where-Object { [IO.Path]::GetFileName($_) -ne $name }) + @($destination)
}
