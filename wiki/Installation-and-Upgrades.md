# Installation and Upgrades

## Requirements

- A current IW4MAdmin installation targeting .NET 10.
- `DemosToDiscord.dll` from the latest release.
- A Discord webhook for notifications and file delivery.
- Read access to the Plutonium demo folders used by the IW4MAdmin host account.
- `Moderator` permission or higher for the webfront dashboard by default.

## First installation

1. Download `DemosToDiscord.dll` from the [latest release](https://github.com/OllyMc27/DemosToDiscord/releases/latest).
2. Stop IW4MAdmin completely.
3. Copy the DLL directly into `IW4MAdmin/Plugins`.
4. Remove older duplicate DemosToDiscord DLLs.
5. Start IW4MAdmin once to generate `Configuration/DemosToDiscord.json`.
6. Stop IW4MAdmin and edit that configuration.
7. Set `Webhook`, `T5DemoPath`, `T6DemoPath` and `TimeZone`.
8. Start IW4MAdmin again.

The startup console should include a line similar to:

```text
[DemosToDiscord] by OllyMc27 loaded. Version: 3.0.0
```

## First-run checks

1. Run `!dtdstatus` in game.
2. Run `!dtdtest` as a SeniorAdmin to test the default webhook.
3. Open **Admin → Demo Evidence** in the webfront.
4. Submit a test report on a T6 multiplayer server.
5. Wait for the match to end and the demo to finish writing.
6. Confirm the original `.demo` and `.json` appear in Discord.
7. Open the case and test the Discord download link.

For proactive review, first back up `Database.db`, set `ProactiveDetection.Enabled` to `true`, restart, then run `!dtdbaseline`. The first baseline build scans the existing T6/IW5 tracked-hit history and can take longer than later incremental refreshes.

## Demo-folder permissions

The account running IW4MAdmin must be able to list and read the configured folders. If IW4MAdmin runs as a Windows service, the path under your interactive Administrator account may not be the service account's path.

Typical folders:

```text
C:\Users\Administrator\AppData\Local\Plutonium\storage\t5\demos
C:\Users\Administrator\AppData\Local\Plutonium\storage\t6\demos
```

Use the actual account and location on your server.

## Upgrading

Before a major upgrade, back up:

- `Configuration/DemosToDiscord.json`;
- IW4MAdmin's `Database.db` (plus its `-wal`/`-shm` files when present, after stopping IW4MAdmin);
- `Configuration/DemosToDiscordCases.json` for the first database-backed upgrade.

Then:

1. Stop IW4MAdmin.
2. Replace the existing file with the new `DemosToDiscord.dll`.
3. Keep the filename exactly `DemosToDiscord.dll`.
4. Start IW4MAdmin and verify the version in the loaded list.
5. Compare the release notes and example configuration for new settings.

Do not rename the DLL with a version suffix. On the first database-backed start, the plugin creates its tables and imports missing legacy JSON cases transactionally. It preserves the JSON source, but all subsequent case changes are written to `Database.db`. Version 3 adds migration 2 for baseline and evaluation state; it does not alter IW4MAdmin's own statistics tables.

## Public case links

Set IW4MAdmin's `Webfront.ManualUrl` to its externally reachable address so Discord messages can link directly to evidence cases and player profiles.

Example:

```text
https://admin.example.com
```

Include the scheme and port if required. Restart IW4MAdmin after changing the main webfront configuration.

Next: [[Configuration]] and [[Troubleshooting]].
