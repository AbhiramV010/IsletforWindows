# Islet for Windows

A Dynamic Island-style overlay for Windows: an inverted trapezoid hanging from the top center of
the screen that shows what's playing, Claude Code / Codex session progress, and permission
requests you can approve or deny from the island.

Stack: C# on .NET 8, WPF, `net8.0-windows10.0.19041.0`. See `instruction.md` (gitignored) for
the full spec and milestone plan.

## Layout

| Path | What |
| --- | --- |
| `src/Islet.App/` | The WPF overlay, media module, permission card, named pipe server. |
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
| Hover the island | Expands media to art + previous / play-pause / next. |
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

## Milestones

1. **Overlay (done)**: island window, top-center positioning, per-monitor DPI, always-on-top
   re-assertion, no focus stealing, animated views.
2. **Media (done, needs a Spotify check)**: system media session (Spotify preferred), title,
   artist, art, transport controls, event-driven.
3. **Pipe and shim (done)**: pipe server with user-only ACL, protocol, fail-safe shim, tests.
4. **Claude Code permissions (done, needs a real-session check)**: settings block, permission
   card, FIFO queue, Allow / Deny.
5. **Session progress (done, needs a real-session check)**: working / waiting / done line from
   `Notification`, `PostToolUse`, `Stop`.
6. Codex hooks.
7. Polish: tray menu, start with Windows, fullscreen hide setting, multi-monitor choice.
