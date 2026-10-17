# Islet for Windows

A Dynamic Island-style overlay for Windows: an inverted trapezoid hanging from the top center of
the screen that shows what's playing, Claude Code / Codex session progress, and permission
requests you can approve or deny from the island.

Stack: C# on .NET 8, WPF, `net8.0-windows10.0.19041.0`. See `instruction.md` (gitignored) for
the full spec and milestone plan.

## Layout

| Path | What |
| --- | --- |
| `src/Islet.App/` | The WPF overlay, media module, permission card, named pipe server, Windows Clock mirror, notifications, tray and settings. |
| `src/Islet.Hook/` | Console exe the agents run as a hook command. Also owns the pipe protocol and product constants (`ProductInfo`), which the app references. |
| `tests/Islet.Tests/` | xUnit: protocol, Claude and Codex adapters, shim fail-safe paths, pipe server + ACL, permission queue, Clock parsing, notification reader, settings. |
| `tests/Islet.Tests/fixtures/` | Saved Claude Code and Codex hook payloads piped into the shim by the tests. |
| `scripts/` | `send-test-toast.ps1` sends a test Windows toast. |

## Build, test, run

The machine has .NET 8 *runtimes* in `C:\Program Files\dotnet` but no SDK, so an SDK was
installed user-locally to `%USERPROFILE%\.dotnet` (no admin needed):

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build Islet.sln
& "$env:USERPROFILE\.dotnet\dotnet.exe" test Islet.sln
```

If you later install an SDK system-wide, plain `dotnet` works the same way.

Both exes live in the repo root (gitignored) for manual testing. Run `.\Islet.App.exe`, and point
the agent hooks at `Islet.Hook.exe`. To refresh them (quit the app first, since Windows locks a
running exe):

```powershell
foreach ($p in "Islet.App", "Islet.Hook") {
    & "$env:USERPROFILE\.dotnet\dotnet.exe" publish src\$p -c Release -r win-x64 `
        --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -o .
}
```

The shim is framework-dependent rather than `PublishAot`: AOT needs the MSVC linker, which isn't
installed. It uses source-generated JSON only.

## Claude Code hooks

Print the block for your exe path and paste it into `~/.claude/settings.json` yourself:

```powershell
.\Islet.Hook.exe --print-config claude
```

On Windows, Claude Code runs hook commands through Git Bash (PowerShell if Git Bash is missing).
The printed `"C:/.../Islet.Hook.exe" claude` form works in both, and a test runs that exact
string through Git Bash.

Session progress comes from the other three hooks: `PostToolUse` shows "Working · <tool>",
a permission or elicitation `Notification` shows "Waiting for you", and `Stop` opens an expanded
"Claude is done" card for 8 s (click to dismiss), as the cue to go back to the terminal. Idle
nudges are ignored, and a session that goes quiet for 10 minutes is dropped. With several
sessions running, the island shows the most recent one with a "+N" badge. Each hook adds roughly
200 ms to the tool call it follows, since Claude waits for the shim.

An `AskUserQuestion` shows "Claude has a question" with "Go to your session", and no Allow or
Deny: it has several options, so it can only be answered in the terminal. The card goes away when
you answer there.

Fail-safe: if the app isn't running, the reply is malformed, the decision is `passthrough`, or
anything throws, the shim exits 0 with no stdout and the agent shows its normal prompt. Only a
click (or Enter/Esc with the pointer on the card) produces allow/deny.

## Codex hooks

Written against the Codex hooks reference for `codex-cli` 0.163, as a separate mapper and
serializer from Claude Code's. Print the config and merge it into `~/.codex/hooks.json` yourself:

```powershell
.\Islet.Hook.exe --print-config codex
```

Codex has `PermissionRequest`, `PostToolUse` and `Stop` (no `Notification`), plus its own tools:
`apply_patch` shows the files it touches ("Update src/app.ts, Add notes.txt"), and other tools show
the description Codex supplies. Allow and deny use the same output shape as Claude Code's; the
serializer can't emit `updatedInput`, `updatedPermissions` or `interrupt`, which Codex fails closed
on. Codex's docs don't say which shell runs hooks on Windows, so the printed `commandWindows` is a
bare path with no quotes (valid in both cmd.exe and PowerShell) unless the path has spaces.
Tests run the shim through cmd-style and Git Bash command strings. Codex gets its own colour.

## Controls

| Control | Action |
| --- | --- |
| Hover the island | Expands media to art + previous / play-pause / next. |
| Allow / Deny on the card | Answers the request at the front of the queue. |
| `Enter` / `Esc` | Allow / deny, only while the pointer is on an Allow / Deny card. |
| Tray icon, right-click | Start with Windows, hide in fullscreen apps, choose the monitor, open logs, quit. |
| `Ctrl+Alt+I` | Debug: force a view (idle → compact → expanded → clock → automatic). |
| `Ctrl+Alt+P` | Debug: add a fake permission card with no agent behind it. |
| `Ctrl+Alt+N` | Debug: show a fake notification. |
| `Ctrl+Alt+Q` | Quit. |
| `Islet.App.exe --demo` | Cycle views every 3 s. |

Clicks and keys are ignored for 600 ms after a card appears, so a click aimed at something else
can't land on Allow. A request still unanswered after 30 s gets a pulsing border.

Logs: `%LOCALAPPDATA%\Islet\logs\app.log` and `hook.log`. Settings: `%LOCALAPPDATA%\Islet\settings.json`.

The island re-asserts `HWND_TOPMOST` once a second (`SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE`),
so other always-on-top windows and the taskbar can't bury it. It cannot outrank an
exclusive-fullscreen game, which no non-injected overlay can.

## Tray menu and settings

- **Start with Windows** writes (or removes) a `Run` value in
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. It needs no admin rights, appears in Task
  Manager's Startup tab, and nothing is written until you turn it on.
- **Hide in fullscreen apps** (on by default) hides the island while Windows says a fullscreen app,
  game or presentation is in front (`SHQueryUserNotificationState`, re-asked only when the
  foreground window changes or moves). A permission request and "Claude is done" still break
  through, since they mean something is waiting for you. Music, timers, notifications and the idle tab stay hidden.
- **Show on monitor** picks the primary monitor (default) or any attached one, listed by number
  and resolution. If the chosen monitor is unplugged the island falls back to the primary one and
  moves back when it returns. Placement is in physical pixels, so it is right on any scaling.

## Music

Only music is shown: Spotify, Apple Music, iTunes, YouTube Music desktop wrappers, Tidal,
Deezer, Amazon Music, Groove, foobar2000, MusicBee and a few others, matched by app id. Netflix,
games and other video are ignored, and nothing falls back to "whatever is playing". Browsers all
report as Chrome or Brave, so a browser session counts as music only if it reports an album,
which music players do and video sites don't. Firefox uses a hashed app id and is not recognised.
A session that is playing beats a paused one, and Spotify beats the rest.

## Windows Clock timers, stopwatch and alarms

The island shows the **Windows Clock app's own** numbers; it keeps no time of its own. Windows has
no API for the Clock app's data, so Islet reads the Clock window through UI Automation (the
accessibility layer): strictly read-only, it never opens, clicks, selects or closes anything in
Clock. It checks whether Clock is running every 3 s, and reads the open page about twice a second.

- **Stopwatch page:** the elapsed time and whether it is running (read from `StopwatchTimerText`
  and the play button's label).
- **Timer page:** the running timer with the least time left, with a "+N" badge for others.
- **Alarm page:** the next enabled alarm, when it is within 15 minutes.

Limits, all because Clock only builds the page you are on: Clock must be open, only the page that is
showing can be read, and a timer left counting after you close Clock can't be seen. If Clock is
minimised its numbers may stop updating. An alarm ringing or a timer finishing is a Windows toast,
which the notification mirror shows. Verified against the real Clock app: it is found, and the open
page is detected. The stopwatch page's ids were confirmed; the timer and alarm pages are read by
shape (times and Pause buttons) and haven't been checked against a live timer or alarm.

## Notifications

Windows toasts are mirrored by reading the notification database Windows keeps
(`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`), which is written the moment a
toast arrives, using the SQLite that ships with Windows (`winsqlite3.dll`, so no package). The
official listener API needs an MSIX package identity, and toast banners can't be read through UI
Automation on this Windows build.

- It reads a private snapshot copy, never the live database, and only toasts that arrive after
  Islet starts (history isn't replayed).
- It sees every toast Windows records, including ones Focus Assist keeps off the screen.
- Text only (title and body), and you can't click through to the sending app. The sender's name
  is made readable from its app id, so it can be approximate.
- The log records the sending app's name, never the notification text.

Each notification shows for 6 s (click to dismiss), queues with a "+N" badge, and repeats seen
within 3 s are ignored. `scripts/send-test-toast.ps1` sends a test toast.

## Milestones

1. **Overlay (done)**: island window, top-center positioning, per-monitor DPI, always-on-top
   re-assertion, no focus stealing, animated views.
2. **Media (done)**: system media session (music only), title, artist, art, transport controls,
   event-driven. Checked against Spotify.
3. **Pipe and shim (done)**: pipe server with user-only ACL, protocol, fail-safe shim, tests.
4. **Claude Code permissions (done)**: settings block, permission card, FIFO queue, Allow / Deny.
5. **Session progress (done)**: working / waiting / done from `Notification`, `PostToolUse`, `Stop`.
6. **Codex hooks (done, needs a real-session check)**: separate mapper and serializer, config
   printer, tests. Not yet run inside a real Codex session.
7. **Polish (done)**: tray menu, start with Windows, fullscreen hide setting, multi-monitor choice.
