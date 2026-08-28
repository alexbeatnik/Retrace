# AGENTS.md — guide for AI coding agents (and new contributors)

A compact offline audio player for Windows, wearing the neutral dark card look
of its sister projects WinCleaner and AV. One ~160 KB portable exe,
**zero dependencies, zero toolchains**: it builds with the `csc.exe` compiler
that ships inside Windows (.NET Framework 4.8). Keep it that way. Licensed under
Apache 2.0 (`LICENSE`).

## Build & test

```powershell
.\build.ps1   # builds Retrace.exe with C:\Windows\Microsoft.NET\...\csc.exe
.\test.ps1    # compiles src\ + tests\ into Retrace.Tests.exe and runs it
```

Run **both** after every change. There is no .sln/.csproj and there must not be
one — both scripts glob `src\*.cs` (+ `tests\*.cs`); a new framework reference
means editing the `/r:` lists in **both** scripts. CI
(`.github/workflows/tests.yml`) runs exactly these two scripts on every PR.

`build.ps1` compiles twice on purpose: pass 1 produces an exe that can render
`app.ico` (`--write-icon`, see `src/Branding.cs`), pass 2 embeds it via
`/win32icon`. Do not "optimize" this into one pass by committing an `.ico` — the
point is that the repository holds no binary assets and the taskbar icon can
never drift from the mark drawn in the app. Note that the icon step uses
`Start-Process -Wait`: PowerShell does not wait for a Windows-subsystem exe, so
`& $exe` would return before the file exists.

## Hard constraints

- **C# 5 only** — the built-in compiler (v4.0.30319) rejects anything newer.
  No `$"..."` interpolation, no `?.`/`??=`, no `nameof`, no expression-bodied
  members, no `out var`, no pattern matching, no tuples, no auto-property
  initializers. Anonymous callbacks use `delegate(...) { }` syntax.
- **.NET Framework 4.8 BCL only** — no NuGet, no third-party libraries, and no
  image asset the *build* consumes. Every glyph is GDI+ vector drawing in
  `src/Icons.cs`, and the app mark in `src/Branding.cs` is drawn the same way.
  The PNGs in `screenshots/` are documentation for the README and are the one
  exception; nothing in `src/` may ever reference them.
- **One network call, and only one.** `src/MainForm.Updates.cs` asks
  `api.github.com` for the latest release; nothing else in the app opens a
  socket. No telemetry, no online tag or cover lookup, nothing about the user's
  library leaving the machine. The JSON is read with two regexes rather than a
  parser — there is no JSON in the BCL that 4.8 offers without a reference, and
  two fields do not justify hand-writing one.
- **UTF-8 sources** (`/codepage:65001`); Ukrainian literals are normal. Never
  edit one through PowerShell's `Get-Content`/`Set-Content`: 5.1 reads with the
  system ANSI codepage and writes UTF-8, so every non-ASCII character comes back
  double-encoded — an em dash turns into `â€"` and `Українська` into
  `Ð£ÐºÑ€Ð°Ñ—Ð½ÑÑŒÐºÐ°`, and it compiles cleanly and only shows up on screen.
  Use the editing tools, or Python with an explicit `encoding='utf-8'`.
- Never commit build outputs, `app.ico`, `settings.ini` or `session.m3u8` (all
  gitignored).

## Architecture

Two halves that share nothing but a couple of plain-data types: an audio engine
with no Windows-UI dependency, and a hand-drawn UI with no audio knowledge. The
look has been reworked three times; the engine survived every one of them
untouched, and that is the property to preserve.

| File | Concern |
|------|---------|
| `src/MediaFoundation.cs` | the MF COM interfaces, declared by hand |
| `src/Decoder.cs` | one file as a stream of interleaved float frames |
| `src/WaveOut.cs` | the `winmm` output device and its buffer ring |
| `src/Dsp.cs` | downmix, the ten-band equaliser, FFT, meter ballistics — pure arithmetic |
| `src/AudioEngine.cs` | the per-track thread that joins the three together |
| `src/Playlist.cs` | track order, shuffle, repeat modes, M3U — pure |
| `src/Tags.cs` | ID3v2/ID3v1/Vorbis/MP4 parsing and track durations — pure |
| `src/Util.cs` | time and settings formatting, path handling, shell interop |
| `src/Lang.cs` | the English/Ukrainian string table |
| `src/Theme.cs` | `Palette`, the accent tones derived from it, and every drawing primitive |
| `src/Controls.cs` | cards, tabs, buttons, sliders, the analyser, the track list |
| `src/Icons.cs` | every glyph, as vector paths |
| `src/Branding.cs` | the app mark and the ICO writer |
| `src/MainForm.cs` | `Main()`, single-instance handshake, lifetime, keyboard |
| `src/MainForm.Ui.cs` | the header, the three pages, painting and readouts |
| `src/MainForm.Playback.cs` | what the controls do, and the background tag pass |
| `src/MainForm.Settings.cs` | `settings.ini` and the restored session |
| `src/MainForm.Updates.cs` | the daily GitHub Releases check and the exe swap |
| `src/MainForm.Install.cs` | per-user install/uninstall, shortcuts, Uninstall key |

### The pages

Three: **player**, **equaliser**, **settings**. The playlist is *not* a page —
it sits at the foot of the player page in the slot the sister apps give their
activity log, with its actions along the card's header row rather than under the
list. Everything is a hand-placed absolute layout against `PageH`×`ContentW`;
nothing reflows, because the window is `FixedSingle`.

### Updating and installing

Both are ports of what WindowsStalker does, deliberately kept recognisable so a
fix in one is easy to carry to the other.

`--install` and `--uninstall` are dispatched in `Main()` **before** the
single-instance mutex is taken. They are launched by the main window as it
closes; taking the mutex would mean waiting on the process being replaced.
Neither mode opens the sound card, so a second copy is harmless.

The update flow's two decisions are pure static methods — `AppUpdateDue` and
`IsNewerVersion` — for the sake of `tests/UpdateTests.cs`. Keep them that way.
`IsNewerVersion` returning true for an unparseable tag would be an infinite
reinstall loop, which is why `-beta` and named tags are rejected rather than
guessed at.

`MaybeCheckAppUpdate` is polled from `Tick()` at 25 Hz, so its guards run in
cheap-first order and it must never do real work on the way to deciding it has
nothing to do. `UpdateBusy` is the player's version of "a job is running": a
swap restarts the app, and a restart cuts the audio, so it refuses while
`engine.State == Playing`. Paused and stopped are fair game.

Both the exe swap and the uninstall hand off to a detached `cmd.exe`, because a
running exe can neither overwrite nor delete itself. That is the only place a
child shell is justified.

### Colour

The look is shared with `../AV/src/Theme.cs` and `../WinCleaner/src/Theme.cs`:
navy-tinted near-black surfaces, rounded cards with a soft drop shadow and a
hairline border, Segoe UI throughout, and one saturated accent carrying every
lit state. Keep the three in step — a tone that drifts here is a tone that has
to be explained.

A `Palette` is one accent hue. `Hot`, `Soft` and `OnAccent` are derived from it,
so a new scheme is one line in `Palette.All` and the whole app follows — and
`tests/PaletteTests.cs` checks the derivation stays legible rather than checking
any particular colour. `Theme.Use` raises `Theme.Changed`; `Themed` (the base of
every custom control) subscribes in its constructor and unsubscribes in
`Dispose`. **Nothing may bake a colour in at construction** — read it in
`OnPaint` or the scheme switch will not reach it.

The surfaces (`Bg`, `Card`, `CardLine`, `Sunken`, `Subtle`), the text tones
(`Text`, `Muted`, `Disabled`) and the three state colours (`Good`, `Warn`,
`Danger`) are deliberately *not* derived: a dark UI is dark whatever the accent
is, and a warning has to mean the same thing in every scheme.

There was an amber CRT skin here before this one — phosphor text, scanlines over
every surface, dashed rules, square corners and letter-spaced monospace. It read
as a prop rather than as something to keep open all day, and WinCleaner had
already made the same move. Do not reintroduce the pieces of it: `Theme` has no
scanline layer, no corner brackets and no letter-spacing, and `Theme.DrawLabel`
and friends are plain `TextRenderer` wrappers on purpose.

### The traps this code already hit

Each cost a debugging round; do not reintroduce them.

- **A Source Reader belongs to the thread that made it.** MF objects created on
  the UI thread are bound to its single-threaded apartment, and every later call
  from the audio thread crosses apartments and throws. `AudioEngine.Run` catches
  broadly and reads a throw as end-of-track, so the symptom is a file that loads,
  reports its duration correctly, and stops instantly. The decoder is therefore
  created, used **and disposed** on the audio thread; `Play()` hands the path
  over and waits on an event for the verdict rather than opening anything itself.
- **A `UserPaint` control's `OnPaint` must call `base.OnPaint(e)` if anything
  attaches to its `Paint` event.** The base implementation is what raises it.
  `Ground` and `Card` both do, because the header wordmark, the status line and
  every card readout are drawn that way — without the call they silently vanish.
- **Dock order is reverse z-order.** Docked children are laid out from the
  *highest* index down, each taking a bite out of what is left. `pageHost` must
  sit at index 0 (`Controls.SetChildIndex(pageHost, 0)`) or it swallows the whole
  client area and the header ends up underneath the pages.
- **`OnTextChanged` does not repaint a `UserPaint` control.** Only
  `ResizeRedraw` invalidates, and a translated caption can measure to the same
  width, in which case the resize never happens and the control keeps painting
  the old language. `NavTab.FitWidth` and `Btn.FitWidth` both call
  `Invalidate()` for exactly that reason.
- **A measured width is not a drawn width.** `TextRenderer.MeasureText` with
  `NoPadding` returns less than `DrawText` will actually use, so an auto-sized
  control fitted to the measurement exactly gets an ellipsis instead of its last
  two characters. `NavTab.FitWidth` and `Btn.FitWidth` both carry slack for it.
- **The clocks are set in a monospace face on purpose.** `Theme.Digits` is a
  second font stack used for the position readout, the duration beside it and the
  list's time column. In Segoe UI the digits are not all the same width, so a
  readout redrawn 25 times a second visibly jitters sideways.
- **The level meter's amber and red are not derived from the scheme.**
  `Analyser.SegmentColour` uses `Theme.Warn` and `Theme.Danger` for the top of
  the range on purpose: "approaching clipping" has to mean the same thing in
  every palette, so those two stay fixed while the rest of the row follows the
  accent.
- **A list must be a whole number of rows tall.** A fraction of a row over shows
  a sliced track along the bottom edge, which reads as a clipping bug rather than
  as more to scroll. `BuildListCard` snaps the height to `TrackList.RowH`.
- **`waveOutGetPosition` is the only honest clock.** Frames handed to
  `waveOutWrite` sit in the queue for its whole depth before they are heard, so
  counting what was written runs a fifth of a second ahead of the music. It also
  returns an unsigned count, and it may answer in a different unit than the one
  asked for — check `wType`.
- **A track that ended is not a track that was stopped.** `Playlist.Next` takes
  an `automatic` flag: repeat-one repeats only for the former, and only the
  former stops at the end of an un-repeated list. Losing that distinction makes
  the next button useless under repeat-one.
- **Adding a file already in the list is still a request to play it.**
  `Playlist.Add` returns -1 when everything was a duplicate; `AddAndPlay` falls
  back to `IndexOf`. Without that, opening a file from Explorer does nothing
  whenever the restored session happened to contain it.
- **A band above Nyquist folds back.** `Biquad.SetPeaking` refuses a centre
  frequency at or above half the sample rate and becomes a pass-through; left to
  the formula, the 16 kHz slider on a 22 kHz file becomes a spurious boost in the
  audible range.

## Working rules

- **`audio-thread`** — everything the UI reads from the engine (position,
  levels, the analyser window) is either an interlocked scalar or guarded by
  `levelLock`. The UI never blocks on the audio thread and the audio thread never
  touches a control. A seek is a request the audio thread performs at the top of
  its next block, never a call into the decoder from the message loop.
- **`palette`** — read colours from `Theme` inside `OnPaint`. A new scheme is one
  line in `Palette.All`; if it needs a hand-picked tone the derivation is wrong
  and should be fixed for every scheme instead.
- **`untrusted-input`** — every length in a tag comes out of the file. Check it
  against what is actually there before reading, and return an empty tag rather
  than throwing: an untagged or truncated file is an ordinary outcome. New format
  support needs its counterpart in `tests/TagsTests.cs`, including a truncation
  and a lying-length case.
- **`duration`** — `Tags.Read` answers how long a track runs as well as what it
  is called, because the decoder only ever opens the track being played and
  every other row would sit at `--:--`. Each container has its own reader in the
  "How long it runs" section of `src/Tags.cs`; zero means "not stated", which is
  a legitimate answer the decoder fills in later, and it must never become a
  guess. `StartTrack` still overwrites it from the decoder, which is the last
  word.
- **`localization`** — every user-visible string goes through `Lang.T("key")`,
  added in `src/Lang.cs` with **both** English and Ukrainian.
  `tests/LangTests.cs` checks key parity, empty strings, `{0}` agreement, and
  that the Ukrainian side is not still the English one.
- **`settings-key`** — a `settings.ini` key needs a parser line in
  `LoadSettings()` and a writer in `SaveSettings()`, and must degrade gracefully
  when missing from an older file. Values are range-checked on the way in; a
  corrupt file must never stop the player from starting. Writes go through
  `WriteAtomic`, so a crash cannot leave a truncated file behind.
- **`testing`** — testable logic is exposed as `internal static` and covered in
  `tests/*.cs` (zero-dependency reflection runner: every public static `Test*`
  method on a `*Tests` class runs). Prefer property tests over the whole preset
  table, the whole palette set or the whole string table to hand-picked cases.
- **`verify`** — after a UI change, launch the built exe and look at it.
  `PrintWindow(hwnd, hdc, 2)` captures a window without stealing focus and works
  even when it is covered. Synthetic clicks via `mouse_event` do **not** work on
  a window that is not foreground, and a posted `WM_LBUTTONDOWN` reaches only the
  window it is addressed to — every custom control here has its own HWND, so a
  click meant for a nav tab must be posted to that tab, not to the form. Driving
  a page switch through `settings.ini` and a restart is usually simpler.
