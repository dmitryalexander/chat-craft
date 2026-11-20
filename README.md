# Chat Craft

A native Windows interface for your own Codex chats, with procedural smoke,
flowing text playback and dial-up-inspired sound.

**[Download for Windows](https://github.com/dmitryalexander/chat-craft/releases/latest)**

**Press F1 in the app to see all keyboard controls and mouse gestures.** Press F1 or Esc to close help.

## Requirements

- Windows x64, .NET Framework 4.8 and a Direct3D 11-capable graphics driver.
- Your own installed, signed-in Codex Desktop and an existing saved chat.
- Your own eligible Codex access; this app does not include an AI subscription.
- Keep Codex Desktop open with the selected chat when using Desktop-owned chats.

Tested on the developer's Windows computer. Compatibility with other computers
is not yet verified. Desktop chat forwarding uses a version-sensitive local
protocol and may require updates when Codex Desktop changes.
This is an independent project, not an official OpenAI application.

## Build

Install Visual Studio 2022 Build Tools with Desktop development with C++,
a Windows 10/11 SDK (including fxc.exe), and the .NET Framework 4.8 runtime.
Run in Windows PowerShell 5.1 from a writable checkout:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1
~~~

Build.ps1 compiles the native shaders and graphics DLL and the managed app.
No NuGet packages, Python, external fonts, model downloads or other Craft app
installations are required. Japanese kanji audio readings optionally use the
installed Windows Japanese IME. Missing fonts use Windows font fallback.
The WordCraft and PageCraft folders contain the shared source needed for this
app, not complete distributions of those applications.

## New in 0.20.6

- F1 opens a dark keyboard-controls overlay with rounded keycaps and compact spacing. Closing it correctly resumes previously playing white reply text.
- Ctrl+Tab opens the latest reply at the beginning, paused. Press Space to play. Ctrl+Shift+Space restarts with playback.
- Mouse wheel scales only the simulation from 25% to 100% inside the fixed window. The black surround reveals rounded canvas corners.
- Caps Lock includes master volume, optional background sound layers and Low/Medium/High/Ultra graphics quality. New downloads start at Medium.

## Controls

| Control | Action |
| --- | --- |
| F1 | Keyboard controls overlay; Esc closes it |
| Ctrl+O | Choose an existing saved Codex chat |
| Enter | Send draft to the selected chat |
| Space during reply | Pause/resume |
| Shift held | Slower reply playback |
| Ctrl+Shift+Space | Replay latest loaded reply |
| Ctrl+Tab | Open latest reply at the beginning, paused; Space plays |
| Shift+left-click/drag | Seek through latest reply |
| Ctrl+left-drag vertically | App sound volume |
| Caps Lock | Sound volume, background sound, graphics quality and colors |
| Mouse wheel | Scale centered simulation canvas 25–100% inside fixed window; black surround |
| Backtick or tilde | Native fullscreen, independently of simulation zoom |

## Privacy and local files

The source distribution includes code, an icon, CC0 sound excerpts and visual
presets. Background noise is synthesized locally without saved audio files. It contains no developer chats, credentials, logs or captured replies.
Chat Craft accesses the current user's Codex account and saved chats through
the locally installed Codex executable and Desktop connection. Messages sent
through Codex are subject to that user's Codex configuration and service.

Settings and error logs are stored in the writable application folder tree.
Do not install into a protected directory. Never commit generated logs,
local settings changes, chat data or credentials. Do not include your .codex
folder when sharing a build.

## Checks

After building, the following silent checks do not send a chat message:

~~~powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Reply-Deposit-Threshold.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Reply-Performance.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Playback-Gestures.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Runtime-Settings.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Replay-Window-Zoom.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\ChatCraft\Tools\Test-Help-Overlay.ps1
~~~

Test-Codex-Discovery.ps1 is a separate opt-in integration test: it reads your
local saved-chat list without selecting a chat or sending a message.
Actual second-computer setup, desktop performance and audio-device behavior
remain to be tested.

## License and credits

Code is MIT licensed; see LICENSE. Sound excerpts are CC0; see
THIRD-PARTY-NOTICES.md. Open-source licensing permits compiling and
redistributing this software, including compiled builds.

## Package a Windows download

After building, run:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1
~~~

The dist folder receives a ZIP with the compiled app, graphics DLL, visual and
reading presets, a double-click launcher, setup notes, license and file hashes.
Existing package outputs are never overwritten. Extract the entire ZIP before
running Open Chat Craft.cmd. The ZIP is unsigned; Windows may show a publisher
or reputation prompt. No app binaries are committed to this repository.
