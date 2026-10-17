# Islet for Windows

A Dynamic Island-style overlay for Windows: a pill at the top center of the screen that will
show Spotify playback, Claude Code / Codex session progress, and permission requests you can
approve or deny from the pill.

Stack: C# on .NET 8, WPF, `net8.0-windows10.0.19041.0`. See `instruction.md` (gitignored) for
the full spec and milestone plan.

## Layout

| Path | What |
| --- | --- |
| `src/Islet.App/` | The WPF overlay: pill window, tray, named pipe server. |
| `src/Islet.Hook/` | Console exe the agents run as a hook command (milestone 3+). |
| `tests/Islet.Tests/` | xUnit tests for the pipe protocol and the shim (milestone 3+). |

## Build and run

The machine has .NET 8 *runtimes* in `C:\Program Files\dotnet` but no SDK, so an SDK was
installed user-locally to `%USERPROFILE%\.dotnet` (no admin needed):

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build Islet.sln
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project src\Islet.App
```

If you later install an SDK system-wide, plain `dotnet` works the same way.

To refresh the single-file `Islet.App.exe` in the repo root (gitignored, kept there for quick
manual testing — double-click it, don't move it):

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish src\Islet.App -c Release -r win-x64 `
    --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o .
```

## Debug controls (milestone 1)

| Control | Action |
| --- | --- |
| `Ctrl+Alt+I` | Cycle pill state: collapsed → compact → expanded. |
| `Ctrl+Alt+Q` | Quit. |
| `Islet.App.exe --demo` | Cycle states every 3 s, no hotkeys needed. |

Logs: `%LOCALAPPDATA%\Islet\logs\app.log`.

The pill re-asserts `HWND_TOPMOST` once a second (`SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE`), so
other always-on-top windows and the taskbar can't bury it. It cannot outrank an exclusive-fullscreen
game, which no non-injected overlay can.

## Milestones

1. **Overlay (done)** — pill window, top-center positioning, per-monitor DPI, always-on-top
   re-assertion, no focus stealing, animated states via debug hotkey.
2. Media — Spotify title, artist, art, transport controls.
3. Pipe and shim — named pipe server with user-only ACL, protocol, fail-safe shim, tests.
4. Claude Code permissions end to end.
5. Session progress from `Notification`, `PostToolUse`, `Stop`.
6. Codex hooks.
7. Polish — tray menu, start with Windows, fullscreen hide setting, multi-monitor choice.
