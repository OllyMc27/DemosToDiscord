# DemosToDiscord Wiki

DemosToDiscord is an IW4MAdmin plugin that turns player reports and automated anti-cheat bans into organised evidence cases. It finds the relevant Plutonium demo when the game supports one, uploads the original files to Discord, and gives moderators a structured webfront review workflow.

This Wiki covers DemosToDiscord 2.3.x.

![DemosToDiscord evidence queue](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/webfront-evidence-queue.png)

## Start here

| Guide | What it covers |
|---|---|
| [Installation and Upgrades](Installation-and-Upgrades) | Requirements, first installation, upgrading and first-run checks. |
| [[Configuration]] | Every configuration setting, accepted values and complete examples. |
| [Language and Timezones](Language-and-Timezones) | Current language support, timestamp format and timezone choices. |
| [Evidence Workflow](Evidence-Workflow) | Reports, automated bans, manual bans and case lifecycle. |
| [Demo Matching](Demo-Matching) | Filename parsing, T6 GUID confirmation, mode fallback and missing demos. |
| [Webfront Guide](Webfront-Guide) | Evidence queue, filters, case review, metrics and admin actions. |
| [Discord Integration](Discord-Integration) | Webhooks, routing, role mentions, attachments and case links. |
| [Admin Commands](Admin-Commands) | Every in-game command and required permission. |
| [Privacy and Data](Privacy-and-Data) | What is stored, what is not stored and recommended protections. |
| [[Troubleshooting]] | Common startup, demo, Discord and webfront problems. |
| [Product Tour](Screenshots) | A visual tour of the evidence workflow. |

## At a glance

- Uploads original T5 and T6 `.demo` files without renaming or ZIP archives.
- Includes the matching T6 `.json` metadata when available.
- Captures configured T6 automated anti-cheat bans even without a player report.
- Groups reports and detections from the same player and match into one case.
- Keeps T4, IW5 and T5 Zombies reports as metadata-only cases.
- Adds **Admin → Demo Evidence** to the IW4MAdmin webfront.
- Shows demo downloads, match timelines, player statistics and anti-cheat metrics.
- Supports assignment, review decisions, notes and case-scoped report clearing.
- Sends Discord evidence messages with direct case and player-profile links.
- Supports default, per-game and per-server webhook routing.

## Supported evidence

| Game/session | Webfront case | Demo upload |
|---|---:|---:|
| T6 multiplayer | Yes | Yes |
| T5 multiplayer | Yes | Yes |
| T5 Zombies | Yes | No—metadata only |
| T4 | Yes | No—metadata only |
| IW5 | Yes | No—metadata only |

## Quick start

1. Download `DemosToDiscord.dll` from the [latest release](https://github.com/OllyMc27/DemosToDiscord/releases/latest).
2. Copy it to `IW4MAdmin/Plugins`.
3. Restart IW4MAdmin.
4. Edit `Configuration/DemosToDiscord.json`.
5. Set the Discord webhook and the T5/T6 demo paths.
6. Restart IW4MAdmin and open **Admin → Demo Evidence**.

Continue with [[Installation and Upgrades|Installation-and-Upgrades]] or jump directly to the complete [[Configuration]] reference.
