# Retrace

A compact offline audio player for Windows — **one ~155 KB portable executable
with zero dependencies**.

No .NET to download, no NuGet packages, no toolchain: the whole thing is built
by `csc.exe`, the C# compiler that already ships inside Windows. Clone the
repository, run `build.ps1`, and you have the app. There is no MSI either —
installing is the app copying itself into your own profile, and it stays the
same portable exe either way.

There is no telemetry and nothing about your library ever leaves the machine.
The one thing that touches the network is the update check: once a day it asks
the GitHub Releases API whether a newer build exists. It is a setting, and it
can be switched off on the Settings page.

It wears the phosphor-terminal look of [WindowsStalker](https://github.com/alexbeatnik/WinCleaner)
and [AV](https://github.com/alexbeatnik/AV) — dark cards, one phosphor hue,
dashed rules and corner brackets — so the three read as the same machine. The
hue is a setting: six schemes, switchable at run time.

```
 ┌ RETRACE ──────────────────── PLAYER  EQUALISER  SETTINGS ┐
 │ ┌ NOW PLAYING ──────────────────────────────────────────┐│
 │ │  Artist — Title of the track                    1:23  ││
 │ │  Album · 2019 · track 3 of 81                   4:12  ││
 │ │  ████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  ││
 │ └───────────────────────────────────────────────────────┘│
 │ ┌ TRANSPORT ──────────┐┌ ANALYSER ─┐┌ LEVELS ───────────┐│
 │ │ ⏮ ▶ ■ ⏭ ⏏  ⤨ ⟳     ││  ▁▃▅█▆▃▁  ││ VOLUME  ███░  70% ││
 │ └─────────────────────┘└───────────┘└───────────────────┘│
 │ ┌ FORMAT   BITRATE   SAMPLE RATE   CHANNELS   TRACK ────┐│
 │ │  FLAC     1006k     44.1kHz       Stereo     3 / 81   ││
 │ └───────────────────────────────────────────────────────┘│
 │ ┌ PLAYLIST  81 · 5:12   FILES FOLDER LOAD SAVE REM CLR ─┐│
 │ │  1  Artist — Title                              4:12  ││
 │ │  2  Artist — Another                            3:48  ││
 │ └───────────────────────────────────────────────────────┘│
 └──────────────────────────────────────────────────────────┘
```

## What it plays

mp3, wav, flac, m4a, m4b, aac, wma, mp4, mkv — and ogg, oga, opus and webm
wherever the **Web Media Extensions** are installed from the Microsoft Store.

Decoding is done by Media Foundation, the codec stack already inside Windows,
so there is no ffmpeg to ship and no codec pack to install. A file Windows has
no decoder for is skipped rather than stalling the playlist.

## Features

| | |
|---|---|
| **Transport** | play, pause, stop, previous, next, seek, eject, shuffle, three repeat modes |
| **Playlist** | on the main page, Ctrl and Shift selection, drag and drop of files and folders, recursive folder adding, M3U8 load and save |
| **Equaliser** | ten ISO octave bands and a preamp, ten presets, with the response curve plotted under them |
| **Analyser** | spectrum or oscilloscope, click to change |
| **Schemes** | amber, green, ice, violet, ember and bone — Ctrl+T cycles them |
| **Languages** | English and Ukrainian |

Volume, balance, the equaliser curve, the colour scheme, the language, the page
you were on and the playlist itself all come back after a restart.

## Keyboard

| Keys | Action |
|---|---|
| Space | play / pause |
| ← / → | seek 5 seconds |
| Ctrl + ← / → | previous / next track |
| ↑ / ↓ | volume |
| Delete | remove the selected tracks |
| Ctrl + O | add files |
| 1 / 2 / 3 | player, equaliser, settings |
| Ctrl + T | next colour scheme |

Double-click a track to play it. Right-click one to reveal it in Explorer.
Double-click the balance bar to centre it. Click the analyser to change what it
shows.

## Build

```powershell
.\build.ps1   # builds Retrace.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into Retrace.Tests.exe and runs it
```

Requires nothing but Windows itself (.NET Framework 4.8, present since Windows
10 1903). `build.ps1` runs the compiler twice on purpose: the app draws its own
icon, so the first pass produces an executable that can write `app.ico` and the
second embeds it. That is why the repository contains no binary assets at all —
every glyph is GDI+ vector drawing in `src/Icons.cs`.

## How it works

```
src/
  MediaFoundation.cs  the COM interfaces, written out by hand
  Decoder.cs          one file, opened as a stream of float frames
  Dsp.cs              downmix, the ten-band equaliser, FFT, meter ballistics
  WaveOut.cs          the output device
  AudioEngine.cs      the thread that joins them up
  Playlist.cs         track order, shuffle, repeat modes, M3U
  Tags.cs             ID3v2, ID3v1, Vorbis comments, MP4 atoms
  Theme.cs            the palettes and the drawing primitives
  Controls.cs         cards, tabs, buttons, sliders, the analyser, the list
  Icons.cs            every glyph, as vector paths
  MainForm*.cs        the window, the pages, playback wiring, saved state
  MainForm.Updates.cs the daily GitHub check and the exe swap
  MainForm.Install.cs the per-user install and uninstall
```

**Audio.** Media Foundation's Source Reader is asked for 32-bit float PCM
rather than for whatever the file happens to hold, so it inserts the decoder and
a converter as needed and every format arrives in the same shape. One thread per
track pulls frames, folds them to stereo, runs the equaliser, takes the levels
the analyser is drawn from, converts to 16-bit and hands blocks to `waveOut` —
which blocks it, and is therefore what paces the whole loop to real time. The
device is opened at the file's own sample rate; Windows' own mixer resamples to
the endpoint far better than anything worth writing here.

The decoder is created, used and destroyed entirely on that thread. This is not
tidiness: a Source Reader built on the UI thread is bound to its single-threaded
apartment, and every later call from the audio thread has to cross apartments —
which throws, and looks exactly like a track that loads, reports its duration
correctly, and stops instantly.

**Colour.** A scheme is one base hue; the headings, captions, rules and the
ink used on a filled block are all derived from it, so adding one is a single
line in `Palette.All` and the whole app follows. Changing it raises an event
that every control repaints on — nothing bakes a colour in at construction.

**Tags** are parsed directly — ID3v2.2/2.3/2.4, ID3v1, Vorbis comments in FLAC
and Ogg, and iTunes atoms in MP4. Media Foundation could supply them, but that
means instantiating a decoder per file, which is far too slow for a folder of a
thousand tracks. Every length in those formats comes out of the file itself and
is checked against what is actually there before a byte is read.

## Tests

```powershell
.\test.ps1
```

92 tests over the parts that have no window: time and settings formatting, the
M3U round trip, playlist order under shuffle and the three repeat modes, the
equaliser and the FFT, the downmix and the level controls, all four tag formats
including truncated and lying headers, the palette derivation, and the two
string tables. No audio device and no real media file is needed, and the suite
runs in about a second.

`.github/workflows/tests.yml` runs the build and the tests on every pull request
and every push to `main`.

## Release

`.github/workflows/release.yml` publishes `Retrace.exe` to a GitHub Release
whenever the version in `src/AssemblyInfo.cs` changes on `main`. It no-ops if a
release for that version already exists.

The builds are not code-signed, so Windows SmartScreen will warn on first
launch. That is expected for a project without a code signing certificate.

## Installing and updating

Both live on the Settings page, and neither needs administrator rights.

**Install for this user** copies the exe into
`%LocalAppData%\Programs\Retrace`, puts a shortcut in the Start menu and on the
desktop, and registers a per-user entry in Apps &amp; features so Windows can
remove it the ordinary way. `settings.ini` and `session.m3u8` are carried across
from the portable folder, but never over files already at the destination — a
reinstall does not wipe your settings. The same button then reads **Remove from
this user** and undoes all of it.

**Automatic updates** ask `api.github.com` for the latest release once on every
launch and once a day after that. If the tag is a plain dotted number newer than
the running build, the release's `Retrace.exe` is downloaded to `%TEMP%` and a
detached `cmd.exe` waits for the player to exit, moves the new build over the old
one and starts it again. Nothing happens while a track is playing — the swap
restarts the app, so it waits until playback is stopped or paused. Every failure
is silent: offline, a rate-limited API, a repository with no releases yet. Only
the **Check for updates** button reports what it found.

## Settings

`settings.ini` and `session.m3u8` sit next to the executable when that folder is
writable — a portable exe on a stick keeps its settings on the stick — and fall
back to `%AppData%\Retrace` when it is not. After an install that means
`%LocalAppData%\Programs\Retrace`, alongside the installed exe.

## License

Apache-2.0.
