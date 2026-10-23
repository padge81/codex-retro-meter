# Codex Retro Meter

A small, always-on-top Windows usage meter for Codex, styled like an amber instrument panel.

- Segmented circular gauge for the five-hour allowance.
- Flat segmented bar for the weekly allowance.
- Red reserve segments for the final 15% of each allowance.
- Live reset countdowns, with Sydney reset times and daylight saving support.
- Automatic usage refresh every minute and a manual refresh button.
- Draggable window, optional always-on-top pin, and system tray controls.
- Optional scheduled recovery for chats stopped by a usage limit.

This is an independent, unofficial project. It is not affiliated with or endorsed by OpenAI.

## Requirements

- Windows with .NET Framework 4.8 installed.
- The Codex desktop app or `codex.exe` installed and signed in with a supported ChatGPT account.
- Windows PowerShell 5.1 to build from source. No NuGet packages or API key are required.

## Build and run

Clone or download this repository, open Windows PowerShell in its folder, then run:

```powershell
.\build.ps1 -Test
.\build\RetroUsage.exe
```

The script uses the .NET Framework C# compiler installed with Windows. It builds the application and runs the recovery-policy tests without contacting your account. There is no installer.

To produce a portable ZIP, including the licence and documentation:

```powershell
.\build.ps1 -Test -Package
```

Builds are also checked on Windows by GitHub Actions. Successful runs provide the portable ZIP as a downloadable workflow artifact.

## Controls

| Control | What it does |
| --- | --- |
| Title area | Drag to move the meter. |
| PIN: ON / OFF | Keep the window above other windows. |
| REFRESH | Fetch current usage immediately. |
| TO TRAY | Hide the window. Double-click its tray icon to restore it. |
| X | Quit the application. |
| AUTO RESUME | Enable or disable requests to the optional recovery scheduler. |

Position and pin preferences are remembered. Reset labels currently use `Australia/Sydney`, rather than the computer's local time zone.

## How usage is read

The meter starts your installed `codex.exe` as a hidden, short-lived app-server process, initializes a local stdio connection, and reads `account/rateLimits/read`. The process is closed after each check. It uses the existing Codex sign-in and does not read or copy the authentication file itself.

The gauges display **remaining allowance as a percentage**. The service does not supply an exact remaining-token balance. Account usage can be shared with other Codex sessions.

If the connection fails, the display identifies the figures as the last known usage. Once a reset time passes, the affected percentage is hidden until a fresh response confirms it. The app never purchases credits or consumes earned resets.

The app-server interface can change between Codex releases. See the [official protocol documentation](https://learn.chatgpt.com/docs/app-server).

## Optional automatic chat recovery

**Auto Resume requires a separate Codex scheduled task. It does not resume chats by itself.** It defaults to **OFF** in a fresh open-source installation.

The meter records when it observes an exhausted allowance and when both five-hour and weekly allowances become available again. A configured scheduled task checks that request every 15 minutes and can continue chats whose latest turn failed because of the observed usage outage.

Completed work, manual interruptions, active chats and approval/input waits are excluded. Billing or workspace-credit failures are flagged for attention rather than treated as resolved by a timed reset. Original approval requirements still apply. Keep the computer awake, Codex running and the meter running. Scheduled checks and resumed chats use normal Codex usage.

See [the setup guide](docs/auto-resume.md) and [scheduler prompt template](docs/resume-monitor-prompt.md). Recovery logic has been tested using simulated exhaustion and recovery. An actual reset-to-chat-resume cycle has not yet been verified end to end.

## Local data

Runtime files live beside the executable, so keep its folder writable:

| File | Contents |
| --- | --- |
| `settings.json` | Window position and pin preference. |
| `usage-cache.json` | Allowance percentages, reset times and last check time. |
| `auto-resume.json` | Recovery switch, running heartbeat and pending recovery request. |
| `auto-resume-results.json` | Created by an optional scheduler: processed recovery IDs and chat-turn results. |
| `connection-check.txt` | Diagnostic result when launched with `--check`. |

These files are ignored by Git. They are not included in the portable package. No credentials are stored by the meter, although the installed Codex process uses its own existing credentials to read the service.

## Development

The source is in `src/RetroUsage.cs`; `tests/RecoveryTests.cs` exercises exhaustion detection, weekly blocking, one-request-per-recovery behavior, disabled mode, missing data and timestamp persistence. Run `build.ps1 -Test` before submitting changes. Live account access can be checked separately with `RetroUsage.exe --check`.

Suggestions, bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[MIT](LICENSE). You may use, modify and redistribute the project under that licence.
