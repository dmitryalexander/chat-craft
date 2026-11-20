param([switch]$ManagedOnly,[string]$OutputName='ChatCraft.exe')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = Split-Path $project -Parent
$app = Join-Path $project 'Bleeding-Edge/App'
$shared = Join-Path $workspace 'WordCraft/Bleeding-Edge/App'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$assemblies = Join-Path $env:WINDIR 'Microsoft.NET/assembly'
$references = foreach ($name in 'PresentationCore', 'PresentationFramework', 'WindowsBase', 'System.Xaml', 'WindowsFormsIntegration') {
    $architecture = if ($name -eq 'PresentationCore') { 'GAC_64' } else { 'GAC_MSIL' }
    $assembly = Get-ChildItem -LiteralPath (Join-Path $assemblies "$architecture/$name") -Filter "$name.dll" -Recurse |
        Select-Object -First 1
    if (!$assembly) { throw "Missing Windows .NET Framework assembly $name." }
    "/reference:$($assembly.FullName)"
}
if([IO.Path]::GetFileName($OutputName) -ne $OutputName -or !($OutputName.EndsWith('.exe'))){throw 'OutputName must be an executable filename.'}
$exe = Join-Path $app $OutputName
if($ManagedOnly -and !(Test-Path -LiteralPath (Join-Path $app 'SumiInkGpu.dll'))){throw 'Managed-only builds require the existing graphics DLL.'}
$icon = Join-Path $app 'ChatCraft.ico'
$morse = Join-Path $app 'DialUpMorse.pcm'
$rendererBuild = Join-Path $workspace 'WordCraft/Tools/Build-WordCraft.ps1'
$sources = 'CodexCraftClient.cs', 'CodexChatPicker.cs', 'WordFlowView.cs', 'FlowMarkdown.cs', 'WordViewEffects.cs',
    'ViewTuning.cs', 'GpuSumiInk.cs', 'GpuPresenter.cs', 'GpuWater.cs', 'GpuFlow.cs', 'SmokeLoadingWorkshop.cs' |
    ForEach-Object { Join-Path $shared $_ }
$pageCraft = Join-Path $workspace 'PageCraft/Bleeding-Edge/App'
$flowSources = 'KineticReader.cs', 'FlowTuningPanel.cs', 'ReaderRaster.cs', 'RuledReader.cs', 'ReaderNarration.cs', 'NarrationEffects.cs' |
    ForEach-Object { Join-Path $pageCraft $_ }
$stage = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('ChatCraft-build-' + [guid]::NewGuid()))
try {
    $gpu = Join-Path $stage.FullName 'SumiInkGpu.dll'
    if(!$ManagedOnly){ & $rendererBuild -NativeOutputPath $gpu }
    . (Join-Path $PSScriptRoot 'Stage-ChatDependencies.ps1')
    $stagedExe = Join-Path $stage.FullName 'ChatCraft.exe'
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /unsafe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll @references "/win32icon:$icon" "/resource:$icon,ChatCraft.ico" "/resource:$morse,DialUpMorse.pcm" "/out:$stagedExe" (Join-Path $app 'JapaneseRomaji.cs') (Join-Path $app 'ChatTypography.cs') (Join-Path $app 'ChatColorPanel.cs') (Join-Path $app 'ChatCraftWindow.cs') (Join-Path $app 'ChatPlaybackGestures.cs') (Join-Path $app 'ChatRuntimeSettings.cs') (Join-Path $app 'ChatWindowZoom.cs') (Join-Path $app 'ChatHelpOverlay.cs') (Join-Path $app 'CloudSmokeView.cs') (Join-Path $app 'FlowSmokeOverlay.cs') $morseSource (Join-Path $app 'DesktopChatConnection.cs') @sources @flowSources
    if ($LASTEXITCODE) { throw 'Chat Craft compilation failed.' }
    if (Test-Path -LiteralPath $exe) {
        $eastern = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Eastern Standard Time')
        $stamp = $eastern.ToString('yyyy-MM-dd_HH-mm-ss')
        $backup = Join-Path $project "Trash/$stamp"
        for ($suffix = 2; Test-Path -LiteralPath $backup; $suffix++) {
            $backup = Join-Path $project "Trash/$stamp-$suffix"
        }
        New-Item -ItemType Directory -Path $backup | Out-Null
        Copy-Item -LiteralPath $exe, $icon, (Join-Path $app 'SumiInkGpu.dll') -Destination $backup
        Write-Output "Previous build preserved in $backup ($($eastern.ToString('zzz')))."
    }
    if(!$ManagedOnly){ Copy-Item -LiteralPath $gpu -Destination (Join-Path $app 'SumiInkGpu.dll') }
    Copy-Item -LiteralPath $stagedExe -Destination $exe
}
finally {
    Get-ChildItem -LiteralPath $stage.FullName -File | Remove-Item
    Remove-Item -LiteralPath $stage.FullName
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $project 'Open Chat Craft.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $workspace
$shortcut.IconLocation = "$icon,0"
$shortcut.Arguments = '' # Native client discovers the current installed package at runtime.
$shortcut.Save()
Write-Output "Built $exe"
