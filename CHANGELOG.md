# Changelog

## 2.3.1

- Preserved the original Plutonium `.demo` and `.json` filenames in Discord attachments so downloaded evidence remains compatible with in-game theatre.
- Changed webfront and Discord timestamps to UK local time in `HH:mm:ss dd/MM/yyyy` format, including daylight-saving handling.
- Prevented manual bans from creating new evidence cases on an unrelated current server; they now only update a recent existing case for that player.
- Added a match timeline showing player join, report, anti-cheat and disconnect times with their offsets into the demo.

## 2.3.0

- Retained reports from T4, IW5 and T5 Zombies as metadata-only evidence cases instead of discarding them.
- Added explicit demo capability detection with configurable game lists and per-server overrides.
- Added queue search plus game, server, source, demo, review and date filters.
- Added case assignment, assigned-to-me and unassigned views.
- Added persisted case activity history, evidence confidence and player evidence history.
- Synced assignment and review outcomes back to the original Discord message while retaining attachments.
- Added per-game and per-server webhook routing and safe optional role notifications.
- Improved unsupported and missing-demo labels throughout the webfront and Discord embeds.

## 2.2.2

- Removed the unreliable in-page evidence section shortcuts from case review pages.
- Increased spacing beneath the evidence queue description.

## 2.2.1

- Redesigned Discord evidence messages with a compact player, match, server and evidence summary.
- Made the embed title open the webfront evidence case when a public URL is configured.
- Added clear review/profile links, uploaded filename and file-size details.
- Improved multi-report and anti-cheat detection presentation.
- Escaped user-provided Discord markdown and retained mention suppression.

## 2.2.0

- Redesigned the evidence queue and case review screens around IW4MAdmin's native full-width layouts.
- Added review-state filters, compact responsive case rows and newer/older case navigation.
- Added a responsive wide-screen workspace while preserving the single-column mobile layout.
- Fixed case links requiring a manual refresh by opting out of same-route enhanced navigation.
- Added direct evidence-case links to new Discord messages when `Webfront.ManualUrl` is configured.
- Improved long server names, filenames, identifiers and review text wrapping throughout the case page.

## 2.1.1

- Fixed an IW4MAdmin startup dependency loop introduced by the evidence review service in 2.1.0.
- The IW4MAdmin manager is now resolved only when an administrator performs a review action.

## 2.1.0

- Redesigned evidence details as a profile-style administrative review page.
- Added native IW4MAdmin profile, statistics, ban, kick, flag and note actions.
- Added persisted review decisions, reviewer attribution, notes and quick-review actions.
- Added case-scoped report clearing without removing unrelated player reports.
- Added aggregate game statistics and seven player anti-cheat metrics to every case.
- Preserved detailed event snapshots for automated anti-cheat ban cases.
- Added separate processing and administrative review states to the dashboard.

## 2.0.2

- Fixed Discord rejecting evidence uploads because the webhook username override contained the reserved word `discord`.
- Evidence messages now use the name configured on the Discord webhook.

## 2.0.1

- Fixed Plutonium demo filename matching on servers whose local timezone differs from UTC, including UK summer time.
- Added detailed debug reasons for rejected demo candidates.
- Added automatic recovery of queued and interrupted evidence searches after IW4MAdmin restarts.

## 2.0.0

- Added automatic T6 anti-cheat ban demo capture.
- Added persistent evidence cases that combine same-match reports and anti-cheat events.
- Added the IW4MAdmin Demo Evidence dashboard with report, match, anti-cheat and Discord data.
- Added fresh Discord CDN attachment links and Discord message links.
- Added background upload workers, deduplication, retry commands and per-server overrides.
- Improved demo selection with target GUID confirmation from T6 JSON metadata.
- Added automated tests and GitHub Actions workflows.
- Demo and JSON files are uploaded directly; archives are not created.

## 1.1.2.4

- Updated for IW4MAdmin 2026 compatibility.

