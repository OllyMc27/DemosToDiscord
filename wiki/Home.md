# DemosToDiscord Wiki

DemosToDiscord is an IW4MAdmin plugin that turns player reports and automated anti-cheat bans into reviewable evidence cases. It locates the relevant Plutonium demo when the game supports one, uploads the original demo and metadata to Discord, and gives moderators a structured review workflow in the IW4MAdmin webfront.

This guide covers DemosToDiscord 2.3.x.

## Contents

- [What the plugin does](#what-the-plugin-does)
- [Requirements](#requirements)
- [Installation and upgrades](#installation-and-upgrades)
- [Quick-start configuration](#quick-start-configuration)
- [Complete configuration reference](#complete-configuration-reference)
- [Per-game and per-server routing](#per-game-and-per-server-routing)
- [Supported evidence by game](#supported-evidence-by-game)
- [How evidence cases are created](#how-evidence-cases-are-created)
- [How demo matching works](#how-demo-matching-works)
- [Using the webfront](#using-the-webfront)
- [Discord integration](#discord-integration)
- [Timezone configuration](#timezone-configuration)
- [Privacy and retained data](#privacy-and-retained-data)
- [Admin commands](#admin-commands)
- [Troubleshooting](#troubleshooting)
- [Screenshot checklist](#screenshot-checklist)

## What the plugin does

The plugin listens for IW4MAdmin penalty events and creates one evidence case per player, server and match. A case can contain:

- one or more player reports;
- an automated anti-cheat ban and its stored metrics;
- an observed manual ban linked to an existing case;
- the original `.demo` file;
- the matching T6 `.json` metadata file;
- player join, report, detection and leave times;
- a Discord message and downloadable attachment links;
- assignment, review decision, reviewer notes and case activity history.

Reports that cannot have a demo are still retained as metadata-only cases, so T4, IW5 and T5 Zombies reports do not disappear from the review queue.

## Requirements

- A current IW4MAdmin installation targeting .NET 10.
- `DemosToDiscord.dll` in the IW4MAdmin `Plugins` folder.
- A Discord webhook for automatic notifications and uploads.
- Read access to the Plutonium demo folders used by the IW4MAdmin host account.
- `Moderator` permission or higher to use the webfront dashboard by default.

The IW4MAdmin process must be able to read the demo directory. If IW4MAdmin runs as a service or under another Windows account, confirm that account can access the configured path.

## Installation and upgrades

1. Download `DemosToDiscord.dll` from the [latest GitHub release](https://github.com/OllyMc27/DemosToDiscord/releases/latest).
2. Stop IW4MAdmin.
3. Copy the DLL into `IW4MAdmin/Plugins`, replacing the previous version if present.
4. Start IW4MAdmin once so `Configuration/DemosToDiscord.json` is generated or updated.
5. Stop IW4MAdmin, edit the configuration, then start it again.
6. Confirm the console lists `DemosToDiscord` as loaded.
7. Run `!dtdstatus` in game and open **Admin → Demo Evidence** in the webfront.

Before a major upgrade, back up:

- `Configuration/DemosToDiscord.json`;
- `Configuration/DemosToDiscordCases.json`.

The case file contains metadata and review history, not copies of the demos.

## Quick-start configuration

The minimum useful configuration is:

```json
{
  "Enabled": true,
  "Webhook": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN",
  "T5DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t6\\demos",
  "TimeZone": "Europe/London",
  "UploadOnReports": true,
  "UploadOnAutomatedBans": true,
  "EnableWebfrontDashboard": true
}
```

Use the paths belonging to the Windows account that records the demos. The [complete example configuration](https://github.com/OllyMc27/DemosToDiscord/blob/master/examples/DemosToDiscord.json) includes every supported setting.

## Complete configuration reference

### General and evidence triggers

| Setting | Default | Purpose |
|---|---:|---|
| `Enabled` | `true` | Enables all evidence collection. |
| `Webhook` | empty | Default Discord webhook. |
| `T5DemoPath` | Plutonium T5 demos | Default World at War multiplayer demo folder. |
| `T6DemoPath` | Plutonium T6 demos | Default Black Ops II multiplayer demo folder. |
| `UploadOnReports` | `true` | Creates cases for player reports. |
| `UploadOnAutomatedBans` | `true` | Creates cases for automated anti-cheat bans. |
| `UploadOnManualBans` | `false` | Allows manual bans to be observed. A manual ban only updates a recent matching case; it does not create an unrelated new case. |
| `AutomatedBanGames` | `[ "T6" ]` | Games for which automated bans create evidence. |
| `SupportedDemoGames` | `[ "T5", "T6" ]` | Games expected to produce downloadable demos. |
| `T5ZombieMapPrefixes` | `[ "zombie_" ]` | T5 map prefixes treated as demo-unsupported Zombies sessions. |
| `T5ZombieModes` | Zombies modes | T5 modes treated as demo-unsupported. |

### Demo search and queue

| Setting | Default | Purpose |
|---|---:|---|
| `MaxLookbackMinutes` | `90` | Oldest demo start time considered for a case. |
| `MaxWaitMinutes` | `30` | Maximum time to wait for the correct demo to appear and become readable. |
| `RetryIntervalSeconds` | `10` | Delay between demo searches and readiness checks. |
| `PostMatchDelaySeconds` | `10` | Extra delay after the file becomes available. |
| `FileStableChecks` | `3` | Consecutive unchanged file-size checks required before upload. |
| `MaxConcurrentUploads` | `2` | Number of evidence workers allowed to upload simultaneously. |
| `DeduplicationWindowMinutes` | `120` | Time window used to group evidence from the same player and match. |

The plugin never renames the source `.demo` or `.json` file. Preserving the original filename keeps downloaded demos compatible with Plutonium theatre.

### Webfront, storage and display

| Setting | Default | Purpose |
|---|---:|---|
| `EnableWebfrontDashboard` | `true` | Registers **Admin → Demo Evidence**. |
| `WebfrontMinimumPermission` | `Moderator` | Minimum permission required to view and review cases. |
| `StoreReportReasons` | `true` | Stores report reason text in the case metadata. |
| `CaseRetentionDays` | `90` | Removes cases older than this retention period. |
| `MaxStoredCases` | `500` | Maximum number of retained cases. Oldest cases are removed first. |
| `StateFilePath` | `Configuration/DemosToDiscordCases.json` | Location of the case state file. Relative paths use the IW4MAdmin directory. |
| `TimeZone` | `Europe/London` | Timezone used for webfront and Discord timestamps. |

### Discord routing and notifications

| Setting | Default | Purpose |
|---|---:|---|
| `SendMetadataOnlyCasesToDiscord` | `true` | Sends cases from games/modes without demo support to Discord. |
| `ReportRoleId` | empty | Optional role mentioned for report evidence. Use the numeric Discord role ID. |
| `AntiCheatRoleId` | empty | Optional role mentioned for anti-cheat evidence. |
| `MentionRolesOnlyWhenDemoReady` | `false` | Delays role mentions until a demo is available. |
| `GameWebhooks` | empty entries | Routes a game to a separate webhook. |
| `ServerOverrides` | empty | Overrides behaviour for an endpoint, legacy server ID or all servers. |
| `Debug` | `false` | Adds detailed demo-search diagnostics and performs the startup webhook test when a default webhook is configured. |

Do not post webhook URLs in screenshots, logs, support messages or public issues. Anyone with the complete URL can send messages through that webhook.

## Per-game and per-server routing

Webhook selection uses this order:

1. the matching server override webhook;
2. the matching `GameWebhooks` entry;
3. the default `Webhook`.

Server overrides can be keyed by:

- endpoint, for example `127.0.0.1:4976`;
- IW4MAdmin legacy server ID;
- `"*"` as a fallback for all remaining servers.

Example:

```json
"GameWebhooks": {
  "T5": "https://discord.com/api/webhooks/T5_ID/T5_TOKEN",
  "T6": "https://discord.com/api/webhooks/T6_ID/T6_TOKEN"
},
"ServerOverrides": {
  "127.0.0.1:4976": {
    "DemoPath": "D:\\Plutonium\\storage\\t6\\demos",
    "Webhook": "https://discord.com/api/webhooks/SERVER_ID/SERVER_TOKEN",
    "SupportsDemos": true,
    "SendMetadataOnlyCasesToDiscord": true,
    "ReportRoleId": "",
    "AntiCheatRoleId": ""
  },
  "*": {
    "SendMetadataOnlyCasesToDiscord": true
  }
}
```

Useful override settings include `Enabled`, `DemoPath`, `Webhook`, the three upload-trigger switches, `SupportsDemos`, metadata-only Discord delivery and role IDs.

## Supported evidence by game

| Game/session | Dashboard case | Demo upload | Notes |
|---|---:|---:|---|
| T6 multiplayer | Yes | Yes | Uses the `.json` sidecar to confirm the target GUID when available. |
| T5 multiplayer | Yes | Yes | Uses strict map, mode and time matching. |
| T5 Zombies | Yes | No | Retained and optionally sent to Discord as metadata-only evidence. |
| T4 | Yes | No | Retained as metadata-only evidence. |
| IW5 | Yes | No | Retained as metadata-only evidence. |

`SupportsDemos` in a server override can explicitly enable or disable demo searching for an unusual server setup.

## How evidence cases are created

### Player report

When IW4MAdmin records a report, the plugin captures the target, reporter, reason, server, game, map, mode and event time. Evidence from the same player and match is grouped into one case.

### Automated anti-cheat ban

For configured games, an automated ban creates evidence even when no player submitted a report. Stored anti-cheat metrics and snapshots are shown on the case page when available.

### Manual ban

A manual ban is linked only to a recent existing evidence case for that client. This prevents a ban initiated while reviewing a demo from accidentally creating a new case against the server the player is currently associated with.

### Unsupported or missing demo

An unsupported game or mode is labelled **Demo unsupported**. A supported match for which no candidate appears before the timeout is labelled **Demo missing**. Both remain reviewable in the webfront.

## How demo matching works

The plugin parses Plutonium's original filename to identify the mode, map and local match start time, then compares it with the evidence event.

For T6 it considers:

- the configured lookback and event window;
- the map reported by IW4MAdmin;
- the mode encoded in the filename as a scoring preference;
- the target network ID in the `.json` metadata as the strongest confirmation;
- proximity between match start and the report or detection.

T6 mode is a preference instead of an absolute rejection because IW4MAdmin and the demo filename can briefly disagree around rotation or map changes. A GUID-confirmed demo on the correct map and time can therefore still be selected.

For non-T6 games, map and mode matching remain strict. Enable `Debug` temporarily to see why nearby candidates were accepted or rejected.

## Using the webfront

Open **Admin → Demo Evidence**.

### Evidence queue

The overview groups cases by review state and includes:

- awaiting, processing, follow-up, cheating, cleared and failed queues;
- unassigned and assigned-to-me views;
- player, case ID, GUID, map and server search;
- game, server, evidence-source, demo-state, review-state and date filters;
- current upload and review badges;
- a direct **Review case** button.

### Case review page

The detailed page contains:

- player identity and profile link;
- server, endpoint, game, map, mode and capture time;
- review state, reviewer, assignment and notes;
- downloadable Discord attachments;
- demo source filename, size, match start and upload time;
- the player join/report/detection/leave timeline and offsets into the match;
- aggregate player statistics and anti-cheat metrics;
- attached reports and report reasons;
- automated detection snapshots where available;
- previous retained cases for the player;
- a case activity audit trail.

The right-hand action panel reuses IW4MAdmin's native profile, statistics, ban, kick, flag and admin-note interactions. Evidence-specific actions let moderators assign the case, save a review result and clear only the reports attached to that case.

### Review decisions

Available decisions are:

- **Needs more review**;
- **Cheating — action taken**;
- **Cheating — no action taken**;
- **Not cheating — no action taken**;
- **Inconclusive**.

Review notes are retained with the case. When an original Discord message exists, assignment and review changes update that message while preserving its attachments.

## Discord integration

New evidence messages include:

- the player and case ID;
- game, map and mode;
- server identity;
- evidence source and confidence;
- review and assignment state;
- demo status and filename;
- match timeline and report positions;
- player reports or anti-cheat detection;
- direct links to the webfront case and player profile.

Set IW4MAdmin's `Webfront.ManualUrl` to the public base address of the webfront so Discord case links work outside the server.

Discord attachment URLs can expire or change. The webfront refreshes the attachment details from the original Discord message rather than storing one CDN URL permanently.

## Timezone configuration

Timestamps use `HH:mm:ss dd/MM/yyyy`. `Europe/London` is the default and automatically handles GMT/BST daylight-saving changes.

Examples:

```json
"TimeZone": "UTC"
```

```json
"TimeZone": "America/New_York"
```

```json
"TimeZone": "Australia/Sydney"
```

Use an IANA timezone identifier. If the identifier cannot be resolved, the plugin logs a warning and falls back to `Europe/London`.

## Privacy and retained data

The state file retains case metadata needed for administration:

- client IDs, network IDs and player names;
- server, game, map and mode;
- report times, reporters and optionally report reasons;
- anti-cheat detection labels and penalty references;
- Discord message identifiers;
- reviewer, assignment, decisions, notes and activity history;
- original demo filename, size and timing metadata.

The plugin does not copy raw demo contents into the state file and does not retain raw Discord CDN URLs there. Webhook secrets remain in the plugin configuration.

Set `StoreReportReasons` to `false` if report text should not be retained. Use a restricted Discord channel because evidence messages can contain player and server identifiers.

## Admin commands

| Command | Permission | Purpose |
|---|---|---|
| `!dtdstatus` | Moderator | Shows enabled state and queue totals. |
| `!dtdstats` | Moderator | Shows evidence totals. |
| `!dtdfind <case-id>` | Moderator | Previews the current best demo candidate. |
| `!dtdtest` | SeniorAdmin | Sends a test through the default webhook. |
| `!dtdretry <case-id>` | SeniorAdmin | Requeues a failed or missing-demo case. |

## Troubleshooting

### The plugin is not shown in IW4MAdmin's loaded list

- Confirm the file is named `DemosToDiscord.dll` and is directly inside `IW4MAdmin/Plugins`.
- Remove older duplicate copies.
- Confirm the DLL was built for the same current IW4MAdmin/.NET generation.
- Restart the complete IW4MAdmin process, not only an individual game server.
- Check the startup log for a dependency or configuration exception.

### A case stays on Searching

- Wait until the match has ended; Plutonium may not finish the demo while the match is active.
- Confirm `T5DemoPath` or `T6DemoPath` points to the folder used by the IW4MAdmin Windows account.
- Confirm the map and event time match a recent filename.
- Temporarily set `Debug` to `true`, restart IW4MAdmin and inspect the demo-scan rejection reasons.
- Use `!dtdfind <case-id>` to preview the current candidate.

### No Discord message appears

- Run `!dtdtest` to check the default webhook.
- Check whether a per-game or per-server webhook is taking precedence.
- Ensure the webhook still exists and has not been regenerated.
- Confirm Discord accepts the file size and that the channel permits webhook messages.
- Check the case's error box and IW4MAdmin log for the complete HTTP response.

### Discord says the webhook username is invalid

Discord rejects webhook usernames containing its reserved service name. DemosToDiscord does not override the webhook's configured display name; edit the webhook name in Discord if an old custom name causes this error.

### The downloaded demo does not appear in theatre

- Keep the original `.demo` and matching `.json` filenames.
- Place both files into the correct Plutonium game demo directory.
- Do not rename the files after downloading.
- Confirm the demo belongs to the same game and Plutonium storage profile.

### Discord's Review link is missing or incorrect

Set IW4MAdmin's `Webfront.ManualUrl` to the externally reachable webfront base URL, including `https://` and any required port. Restart IW4MAdmin after changing it.

### A moderator cannot open Demo Evidence

Check `WebfrontMinimumPermission` and the user's IW4MAdmin level. The interaction requires both read access to the dashboard and write access for review actions.

### A report has no demo

This is expected for T4, IW5 and T5 Zombies. For supported T5/T6 multiplayer, verify the demo path, filename time, map, match end and search timeout. The case remains available even when no file is found.

## Screenshot checklist

The README contains placeholders for the screenshots below. Capture them at a readable desktop width, preferably 1600–1920 pixels, using the same dark theme.

1. `docs/images/webfront-evidence-queue.png`
   - Show the **Demo Evidence** sidebar item, overview counters, filter tabs and at least two case rows.
   - Ideally include one uploaded case and one metadata-only or missing-demo case.
2. `docs/images/webfront-case-review.png`
   - Show the case header, review banner, review summary, demo download and right-hand action panel.
   - Use a case with a working demo attachment.
3. `docs/images/webfront-timeline-metrics.png`
   - Show the match timeline and the player/anti-cheat metric cards together.
   - Include visible “into match” offsets.
4. `docs/images/webfront-metadata-only.png`
   - Show a T4, IW5 or T5 Zombies case labelled as demo unsupported.
   - Keep the reports and review controls visible to demonstrate that the case is still actionable.

Before committing screenshots, hide or blur public IP addresses, webhook URLs and any player identifiers you do not want published. Keep player data consistent across screenshots so the README reads like one workflow.
