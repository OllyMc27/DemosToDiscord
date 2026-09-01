# Webfront Guide

Open **Admin → Cheating Case Review**. Access requires `WebfrontMinimumPermission`, which defaults to `Moderator`.

## Evidence queue

The compact overview provides four status queues without immediately loading every retained case. Select a queue to reveal its cases. The case browser provides:

- Awaiting, Processing, Follow-up, Cheating, Cleared and Failed views;
- Unassigned and Assigned to me views;
- player, case ID, GUID, friendly/raw map and server search;
- game, server, source, demo-state, review-state and date filters, hidden beneath **Search and filters** until requested;
- upload and review badges;
- a direct **Review case** action.

Cases remain visible when demos are unsupported or missing.

![Cheating Case Review overview with compact status and utility queues](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/webfront-v2-4-overview.png)

## Case browser and filters

The overview never expands every retained case by default. Selecting a status or utility queue opens the case browser. Its **Search and filters** panel is collapsed on every visit until requested, keeping routine triage compact.

## Case header

The case header shows:

- player name, client ID and network ID;
- game and processing/review status;
- server name and endpoint;
- IW4MAdmin's friendly map and mode names, with raw game identifiers directly underneath;
- capture and last-updated time;
- evidence source and confidence.

The profile link opens IW4MAdmin's normal client profile.

![Structured evidence case review](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/webfront-v2-4-case-review.png)

## Demo evidence

When Discord delivery succeeds, the case page retrieves fresh attachment information from the original message. Download buttons can include:

- the original `.demo` file;
- T6 `.json` metadata.

The source filename, file size, match start and upload time are also shown. The Discord message link opens the original evidence notification.

## Match timeline

The timeline places these events within the recorded match:

- demo start;
- player join;
- every attached report;
- automated anti-cheat ban;
- player leave.

Each row displays local time and, where possible, the offset into the match—for example `4m 40s into match`.

## Player and anti-cheat metrics

The page can show aggregate kills, deaths, K/D, performance, score per minute, playtime, hit-location percentages, hit offset, strain and snap values.

Automated-ban cases can also show case-specific anti-cheat snapshots. Metrics provide context, not a verdict; always review the available demo and surrounding evidence.

![Match timeline, reports and player anti-cheat metrics](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/webfront-v2-4-timeline-metrics.png)

## Proactive statistical analysis

Proactive cases add an analysis panel containing the 0–100 risk score, risk band and every qualifying indicator. Each indicator includes the observed statistic, its comparable percentile and a plain-language reason it contributed. If a case was created from an ordinary report or ban, it does not pretend that a retrospective proactive assessment exists.

Read [[Proactive Detection|Proactive-Detection]] for sample requirements, supported games and safe threshold tuning.

## ServerPulse community-signal context

Cases created from a manually resolved Player Guidance signal show the accusation, resolving administrator, bounded nearby chat and any review note in a dedicated panel. These cases use the same demo, assignment and decision controls, but they are labelled **ServerPulse community signal** rather than proactive statistical risk.

If **Inconclusive** is selected and `FlagPlayerOnInconclusiveReview` is enabled, the result confirms whether IW4MAdmin accepted the native Flag event. The case history records a successful level change.

## Player actions

The right-hand panel reuses IW4MAdmin's native:

- Open profile;
- View statistics;
- Ban;
- Kick;
- Flag;
- Add admin note.

Native permission checks and confirmation forms still apply.

## Evidence actions

Moderators can:

- assign or unassign a case;
- complete a detailed review with notes;
- use quick cheating/not-cheating decisions;
- mark a case for more review;
- clear the reports attached to the case.

Owners also see **Delete case permanently**. Deletion requires confirmation and removes the retained case metadata only; it does not reverse player penalties or remove the corresponding Discord message.

## Metadata-only cases

T4, IW5, T5 Zombies and explicitly unsupported servers retain the same review controls even when no demo can be recorded. The case explains the capability limitation while preserving reports, metrics, assignment, decisions and activity.

## Other evidence and activity

The lower sections show previous retained cases for the player and an audit trail of creation, evidence, assignment, report-clearing and review changes.

Next: [[Evidence Workflow|Evidence-Workflow]], [[Discord Integration|Discord-Integration]] or [[Screenshots]].
