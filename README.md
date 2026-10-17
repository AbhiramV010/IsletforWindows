# Islet for Windows

A Dynamic Island-style overlay for Windows: an inverted trapezoid hanging from the top center of
the screen that shows what's playing, Claude Code / Codex session progress, and permission
requests you can approve or deny from the island.

Stack: C# on .NET 8, WPF, `net8.0-windows10.0.19041.0`. See `instruction.md` (gitignored) for
the full spec and milestone plan.

## Layout

| Path | What |
| --- | --- |
| `src/Islet.App/` | The WPF overlay, media module, permission card, named pipe server, timers, notifications. |
| `src/Islet.Hook/` | Console exe the agents run as a hook command. Also owns the pipe protocol and product constants (`ProductInfo`), which the app references. |
| `tests/Islet.Tests/` | xUnit: protocol, Claude adapter, shim fail-safe paths, pipe server + ACL, permission queue. |
| `tests/Islet.Tests/fixtures/` | Saved hook payloads piped into the shim by the tests. |

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
a permission or elicitation `Notification` shows "Waiting for you", and `Stop` shows "Done", which
clears after 5 s. Idle nudges are ignored, and a session that goes quiet for 10 minutes is
dropped. With several sessions running, the island shows the most recent one with a "+N" badge.
Each hook adds roughly 200 ms to the tool call it follows, since Claude waits for the shim.

Fail-safe: if the app isn't running, the reply is malformed, the decision is `passthrough`, or
anything throws, the shim exits 0 with no stdout and Claude shows its normal prompt. Only a click
(or Enter/Esc with the pointer on the card) produces allow/deny.

## Controls

| Control | Action |
| --- | --- |
| Hover the island | Expands media to art + previous / play-pause / next. With no music, opens the stopwatch and alarm panel. |
| Clock button (media panel) / note button (timers panel) | Switches between the two panels for that hover. |
| `Ctrl+Alt+S` | Start / pause the stopwatch. |
| `Ctrl+Alt+R` | Reset the stopwatch. |
| `Ctrl+Alt+A` | Set an alarm for 15 minutes from now. |
| `Ctrl+Alt+D` | Stop the ringing alarm. With none ringing, clear all pending alarms. |
| `Ctrl+Alt+N` | Debug: show a fake notification. |
| Allow / Deny on the card | Answers the request at the front of the queue. |
| `Enter` / `Esc` | Allow / deny, only while the pointer is on the card. |
| `Ctrl+Alt+I` | Debug: force a view (idle → compact → expanded → automatic). |
| `Ctrl+Alt+P` | Debug: add a fake permission card with no agent behind it. |
| `Ctrl+Alt+Q` | Quit. |
| `Islet.App.exe --demo` | Cycle views every 3 s. |

Clicks and keys are ignored for 600 ms after a card appears, so a click aimed at something else
can't land on Allow. A request still unanswered after 30 s gets a pulsing border.

Logs: `%LOCALAPPDATA%\Islet\logs\app.log` and `hook.log`.

The island re-asserts `HWND_TOPMOST` once a second (`SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE`),
so other always-on-top windows and the taskbar can't bury it. It cannot outrank an
exclusive-fullscreen game, which no non-injected overlay can.

## Music

Only music is shown: Spotify, Apple Music, iTunes, YouTube Music desktop wrappers, Tidal,
Deezer, Amazon Music, Groove, foobar2000, MusicBee and a few others, matched by app id. Netflix,
games and other video are ignored, and nothing falls back to "whatever is playing". Browsers all
report as Chrome or Brave, so a browser session counts as music only if it reports an album,
which music players do and video sites don't. Firefox uses a hashed app id and is not recognised.
A session that is playing beats a paused one, and Spotify beats the rest.

## Stopwatch and alarms

Both live in the island, since it never takes focus. The compact line shows the stopwatch
(while it has a time on it) and the next alarm (when it is within 15 minutes). The hover panel has
start / pause / reset and `+5 +15 +30 +60` minute alarm presets. A ringing alarm takes over the
island with Snooze (5 min) and Dismiss, plays the system alert sound for up to a minute, and
pulses the border. Pending alarms are saved to `%LOCALAPPDATA%\Islet\alarms.json`, so they survive
a restart; one missed by more than an hour while the app was closed is dropped.

## Notifications

Windows toasts are mirrored by reading the banner when it pops up, through UI Automation. The
proper listener API needs an MSIX package identity, which this app doesn't have. Limits:

- Only banners that actually appear are seen. Toasts held back by Focus Assist (including while
  a fullscreen app is up) are missed.
- Text only, and you can't click through to the sending app.
- **Unverified on real toasts:** the banner's element layout was written from memory, not tested,
  because no banner could be shown while a fullscreen window was up. The log records the window
  structure (never the notification text) when a shell window opens but nothing can be read.

Each banner shows for 6 s (click to dismiss), queues with a "+N" badge, and repeats seen within
3 s are ignored. `scripts/send-test-toast.ps1` sends a test toast.

## Milestones

1. **Overlay (done)**: island window, top-center positioning, per-monitor DPI, always-on-top
   re-assertion, no focus stealing, animated views.
2. **Media (done, needs a Spotify check)**: system media session (Spotify preferred), title,
   artist, art, transport controls, event-driven.
3. **Pipe and shim (done)**: pipe server with user-only ACL, protocol, fail-safe shim, tests.
4. **Claude Code permissions (done, needs a real-session check)**: settings block, permission
   card, FIFO queue, Allow / Deny.
5. **Session progress (done)**: working / waiting / done line from
   `Notification`, `PostToolUse`, `Stop`.
6. Codex hooks.
7. Polish: tray menu, start with Windows, fullscreen hide setting, multi-monitor choice.
