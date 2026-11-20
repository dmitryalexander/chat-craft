param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$folder = Join-Path $OutputDirectory 'ChatCraft-0.18.27-windows-x64'
$zip = "$folder.zip"
if ((Test-Path -LiteralPath $folder) -or (Test-Path -LiteralPath $zip)) { throw 'Output already exists. Choose another output directory.' }
$items = @(
 'ChatCraft/Bleeding-Edge/App/ChatCraft.exe',
 'ChatCraft/Bleeding-Edge/App/SumiInkGpu.dll',
 'ChatCraft/Bleeding-Edge/Color-Tuning.json',
 'ChatCraft/Bleeding-Edge/View-Tuning.txt',
 'ChatCraft/Bleeding-Edge/View-Quality.txt',
 'WordCraft/Bleeding-Edge/Word-Flow-Defaults.json',
 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md')
foreach ($item in $items) {
 if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $item) -PathType Leaf)) { throw "Missing package input: $item. Build first." }
}
foreach ($item in $items) {
 $destination = Join-Path $folder $item
 New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $item) -Destination $destination
}
$launch = @'
@echo off
setlocal DisableDelayedExpansion
pushd "%~dp0"
start "" "ChatCraft\Bleeding-Edge\App\ChatCraft.exe"
popd
'@
[IO.File]::WriteAllText((Join-Path $folder 'Open Chat Craft.cmd'),$launch.Replace([string][char]10,([string][char]13+[char]10)),[Text.Encoding]::ASCII)
$quick = @'
CHAT CRAFT 0.18.27 - WINDOWS X64

Extract the whole ZIP into a writable folder, then double-click Open Chat Craft.cmd.
Keep the folders together. No developer tools are needed to run this package.

Requires .NET Framework 4.8, compatible Direct3D 11 graphics, and your own
installed, signed-in Codex Desktop with an existing chat.
Open that chat in Codex Desktop, then select it in Chat Craft.
Your own Codex access is required; AI access is not included.

Ctrl+O: chat picker. Enter: send. Caps Lock: colors. Backtick: fullscreen.
Space: pause/resume reply. Shift+Space: replay. Shift-drag: seek.
Ctrl-drag vertically: app volume. Close the window to quit.

See README.md for setup details, privacy information and limitations.
Tested on the developer's computer; second-computer compatibility is unverified.
This package is unsigned; Windows may show a publisher or reputation prompt.
'@
[IO.File]::WriteAllText((Join-Path $folder 'START-HERE.txt'),$quick,[Text.Encoding]::UTF8)
$hashes = foreach ($entry in Get-ChildItem -LiteralPath $folder -File -Recurse | Sort-Object FullName) {
 $relative = $entry.FullName.Substring($folder.Length + 1).Replace('\','/')
 (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $relative
}
[IO.File]::WriteAllLines((Join-Path $folder 'SHA256SUMS.txt'),[string[]]$hashes,[Text.Encoding]::ASCII)
Compress-Archive -LiteralPath $folder -DestinationPath $zip -CompressionLevel Optimal
Write-Output "Prepared $zip"
Get-FileHash -LiteralPath $zip -Algorithm SHA256
