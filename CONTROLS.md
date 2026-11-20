# Chat Craft controls — 0.19.1

| Control | Action |
| --- | --- |
| Caps Lock | Open/close Settings: sound, graphics quality and colors |
| Esc in Settings | Save and close Settings |
| Sound volume slider | Master volume for typing/reply cues and background sound; 0 mutes |
| Ctrl+left-drag vertically | Master sound volume, up louder and down quieter |
| Primary background sound | Dark / Darker / Darkest filtered noise |
| Primary background volume | Independent primary layer level; 0 disables it |
| Primary only during playback | Fade primary noise in during reply playback; off also allows idle/typing/thinking ambience |
| Secondary background sound/volume | Independent additive Dark / Darker / Darkest layer |
| Add secondary sound while reading | Fade the second layer in during reply playback and out on pause/end |
| Graphics quality slider | Low / Medium / High / Ultra; lowers GPU resolution and solver effort |
| Ctrl+O | Choose an existing saved Codex chat |
| Enter | Send draft to selected chat |
| Space during reply | Pause/resume |
| Shift held | Slower reading |
| Ctrl+Shift+Space | Replay latest reply from the beginning |
| Shift+Space | Normal space while typing; returns from reply to typing |
| Mouse wheel forward/back | Grow/shrink centered window, 25–100% of monitor width and height |
| Ctrl+Tab | Reopen reply at current position |
| Shift+left-click/drag | Seek through latest reply |
| Backtick or tilde | Toggle fullscreen / last wheel size |

Settings changes apply live and save independently of WordCraft.
Minimizing fades background sound to silence and suspends smoke. Closing stops
owned audio output. Settings and scrub pause the reading-only sound layer.
Background sound is generated locally without WAV files, models or caches.

Quality presets reduce simulation rows to144/288/432/576 and solver iterations
to12/16/22/32. Dye height is capped at540/720/1080/2160 rows respectively.
Text stays sharp and reading speed remains independent. Lower settings reduce
GPU work; a particular frame rate on other computers is not guaranteed.
New download packages start at Medium; existing installed preferences persist.
