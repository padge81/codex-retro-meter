# Optional auto-resume setup

The application detects recovery and writes a request; it does not send messages to chats. A Codex scheduled task performs the chat inspection and continuation. Setting the meter's switch to ON without that task does not install a working resumer.

## Set up the scheduled task

1. Build and run the meter. Choose a permanent writable location for its executable. The containing folder is your **meter directory**.
2. Open a dedicated local Codex chat for monitoring. Identify that chat's ID so the monitor can exclude itself.
3. Open `docs/resume-monitor-prompt.md` from the source repository or extracted portable ZIP.
4. Replace `{{METER_DIRECTORY}}` with the absolute meter directory, and `{{MONITOR_CHAT_ID}}` with that monitoring chat's ID.
5. Ask Codex to create a recurring scheduled task in the monitoring chat, every 15 minutes, using the completed prompt. Explicitly authorize continuation messages to local chats whose latest turn failed because of an observed usage outage.
6. Confirm that the scheduled task exists, then turn **AUTO RESUME: ON** in the meter.

Keep the computer awake and both Codex and the meter running. Scheduled checks use normal Codex usage and are not an instant wake-up mechanism.

## Recovery rules

- The meter must observe five-hour or weekly allowance reaching zero, then a fresh check must show both allowances above zero.
- It records one recovery ID per observed outage. Missing data does not count as recovery.
- The scheduler verifies the meter's switch, recent heartbeat and request before acting.
- It fetches fresh usage and only considers chats with a latest usage-limit failure from the recorded outage.
- It rechecks each chat immediately before sending a continuation. Completed chats, manual interruptions, active work and approval/input waits are skipped.
- It does not assume workspace-credit or billing failures are fixed by a timed reset.
- A ledger written before dispatch prevents automatic duplicate sends after an ambiguous outcome.
- It sends at most three continuation messages per run. A large backlog can take several runs.

Turning the switch OFF clears the pending request. Closing the meter expires its heartbeat; a crashed or sleeping meter's heartbeat expires within three minutes. Disabling the switch does not cancel messages already delivered or work already running. Manage the scheduled task in Codex to remove the recurring check completely.

## Verification status

The policy tests cover normal usage, exhaustion, weekly blocking, recovery, duplicate request prevention, disabled mode, missing data and persisted timestamps. Read-only chat inspection and the on/off switch have been tested. A real usage reset followed by unattended chat continuation has not yet been verified end to end.
