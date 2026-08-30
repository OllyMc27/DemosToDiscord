# Webfront Guide

Open **Admin → Demo Evidence**. Access requires `WebfrontMinimumPermission`, which defaults to `Moderator`.

## Evidence queue

The overview provides:

- Awaiting, Processing, Follow-up, Cheating, Cleared and Failed views;
- Unassigned and Assigned to me views;
- player, case ID, GUID, map and server search;
- game, server, source, demo-state, review-state and date filters;
- upload and review badges;
- a direct **Review case** action.

Cases remain visible when demos are unsupported or missing.

## Case header

The case header shows:

- player name, client ID and network ID;
- game and processing/review status;
- server name and endpoint;
- map and mode;
- capture and last-updated time;
- evidence source and confidence.

The profile link opens IW4MAdmin's normal client profile.

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

## Other evidence and activity

The lower sections show previous retained cases for the player and an audit trail of creation, evidence, assignment, report-clearing and review changes.

Next: [[Evidence-Workflow]], [[Discord-Integration]] or [[Screenshots]].
