![DemosToDiscord — IW4MAdmin evidence review](docs/images/demostodiscord-banner.png)

[![Release](https://img.shields.io/github/v/release/OllyMc27/DemosToDiscord?style=flat-square)](https://github.com/OllyMc27/DemosToDiscord/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/OllyMc27/DemosToDiscord/ci.yml?branch=master&style=flat-square)](https://github.com/OllyMc27/DemosToDiscord/actions)
[![License](https://img.shields.io/github/license/OllyMc27/DemosToDiscord?style=flat-square)](LICENSE)

## Turn player reports into review-ready evidence—automatically

DemosToDiscord connects [IW4MAdmin](https://github.com/RaidMax/IW4M-Admin), Plutonium demos and Discord. It finds the right match recording, groups the surrounding evidence into one case and gives moderators a purpose-built review workflow inside the IW4MAdmin webfront.

Version 3 stores the retained evidence workflow in IW4MAdmin's `Database.db` and adds explainable proactive human review. It evaluates persisted statistics after matches and disconnects, creates review cases only when conservative population safeguards pass, and never punishes automatically.

[Download the latest release](https://github.com/OllyMc27/DemosToDiscord/releases/latest) · [Installation guide](https://github.com/OllyMc27/DemosToDiscord/wiki/Installation-and-Upgrades) · [Complete Wiki](https://github.com/OllyMc27/DemosToDiscord/wiki)

![DemosToDiscord evidence queue](docs/images/webfront-evidence-queue.png)

### One queue. Every case. No digging through Discord history.

- Search and filter by player, case, GUID, game, server, source, demo state, review state or date.
- See uploaded, missing and metadata-only evidence together.
- Share work through unassigned and assigned-to-me queues.
- Group repeat reports and detections from the same match into one case.

## Everything needed to make a decision

![Structured evidence case review](docs/images/webfront-case-review.png)

Each case brings together the original demo, T6 JSON metadata, server and match context, reports, review notes and IW4MAdmin's native player actions. Moderators can assign work, ban, kick, flag, add notes, clear only the attached reports and record a final outcome without leaving the page.

## Proactive review, with the maths shown

- T6 and IW5 compare weapon-specific tracked-hit head rates against eligible players using the same normalized base weapon.
- T4 and T5 multiplayer use a clearly labelled cumulative head-rate fallback; T5 Zombies is excluded.
- Tiny samples, small populations and unchanged statistics cannot create repeat cases.
- Every signal records its percentile, expected value, multiple, sample size, population and score contribution.
- A match-end or disconnect evaluation can create or merge a normal evidence case and attempt the existing demo workflow. It cannot warn, kick or ban.

Set `ProactiveDetection.Enabled` to `true` after backing up `Database.db`. Use `!dtdbaseline` to check cache health and **Admin → Proactive Review** to work the resulting queue.

## Jump straight to the useful moment

![Match timeline and anti-cheat metrics](docs/images/webfront-timeline-metrics.png)

The match timeline shows when recording began, when the player joined, every report or anti-cheat event and when the player left—including the offset into the demo. Player statistics and anti-cheat metrics sit alongside the timeline as context for the review.

## Discord evidence that stays in sync

![Completed evidence review in Discord](docs/images/discord-review-completed.png)

- Uploads the original `.demo` directly—no ZIP archive and no renamed theatre file.
- Includes the T6 `.json` metadata sidecar when available.
- Links straight back to the evidence case and native player profile.
- Updates the original message when a case is assigned or reviewed while keeping its attachments.
- Supports global, per-game and per-server webhooks with optional role mentions.

## No demo support does not mean a lost report

![Metadata-only evidence case](docs/images/webfront-metadata-only.png)

T4, IW5 and T5 Zombies reports remain fully actionable metadata-only cases. Administrators still get the report, player context, statistics, assignment, review controls and audit trail, with a clear explanation that recording is unsupported.

## What it captures

| Game/session | Evidence case | Demo delivery |
|---|---:|---:|
| T6 multiplayer | Yes | `.demo` + `.json` |
| T5 multiplayer | Yes | `.demo` |
| T5 Zombies | Yes | Metadata only |
| T4 | Yes | Metadata only |
| IW5 | Yes | Metadata only |

Configured T6 automated anti-cheat bans can create evidence even when nobody reports the player. Manual bans only attach to a recent matching case, preventing unrelated cross-server evidence.

## Install in minutes

1. Download `DemosToDiscord.dll` from the [latest release](https://github.com/OllyMc27/DemosToDiscord/releases/latest).
2. Copy it into `IW4MAdmin/Plugins`, replacing any older version.
3. Restart IW4MAdmin and edit `Configuration/DemosToDiscord.json`.
4. Set the Discord webhook and T5/T6 demo paths, then restart IW4MAdmin.
5. Open **Admin → Demo Evidence**.

```json
{
  "Enabled": true,
  "Webhook": "https://discord.com/api/webhooks/...",
  "T5DemoPath": "C:\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Plutonium\\storage\\t6\\demos",
  "TimeZone": "Europe/London"
}
```

The [complete configuration reference](https://github.com/OllyMc27/DemosToDiscord/wiki/Configuration) covers queue timing, retention, privacy, timezones, capability overrides, Discord routing, server overrides and every supported setting. Set IW4MAdmin's `Webfront.ManualUrl` to its public address to include working case links in Discord.

## Built for shared administration

- Permission-protected webfront access and native IW4MAdmin confirmation forms.
- Configurable timezone plus per-server collection and Discord-routing overrides.
- Stable-file checks, background processing, deduplication and retry controls.
- Case history, previous player evidence and case-scoped report clearing.
- In-game status, statistics, demo-search, webhook-test and retry commands.

See the [Admin Commands guide](https://github.com/OllyMc27/DemosToDiscord/wiki/Admin-Commands) for command names and permissions.

## Privacy

The plugin retains case metadata and review history—not demo contents or raw chat. Webhook secrets are never written to the case state file or displayed in the webfront. Review the [Privacy and Data guide](https://github.com/OllyMc27/DemosToDiscord/wiki/Privacy-and-Data) before publishing logs or screenshots.

## License

[MIT](LICENSE)
