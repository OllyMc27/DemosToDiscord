# Evidence Workflow

## Player reports

When IW4MAdmin records a report, DemosToDiscord captures:

- target client ID, network ID and name;
- reporter ID and name;
- report reason, unless reason storage is disabled;
- server name and endpoint;
- game, map and mode;
- report time.

Reports and detections for the same player and match within the deduplication window are grouped into one case.

## Automated anti-cheat bans

For games in `AutomatedBanGames`, a recognised automated ban creates evidence even when nobody reported the player. The case can include IW4MAdmin's stored anti-cheat detection and snapshot metrics.

The default list is:

```json
"AutomatedBanGames": [ "T6" ]
```

## Manual bans

A manual ban does not create a new case by itself. It is linked only to a recent existing case for that client. This prevents a ban issued while reviewing one demo from creating an unrelated case on another server.

## Proactive statistical review

Eligible real-player sessions are evaluated after disconnect or match end when proactive detection is enabled. Only assessments at or above `ProactiveCaseRiskThreshold` create or merge a case. They use the normal demo-search and review pipeline, identify the indicators that contributed, and never administer a penalty automatically. Discord delivery uses the separate `ProactiveDiscordRiskThreshold`.

See [[Proactive Detection|Proactive-Detection]] for safeguards, supported signals and exclusions.

## Case lifecycle

1. **Queued** — the case is waiting for a background worker.
2. **Searching** — the demo directory is being checked.
3. **Waiting for demo** — a candidate exists but must finish writing and become stable.
4. **Uploading** — the evidence is being sent to Discord.
5. **Uploaded** — the Discord message and attachments were created.
6. **Demo missing** — the game supports demos, but no candidate appeared in time.
7. **Demo unsupported** — the game/mode is intentionally metadata-only.
8. **Failed** — an error interrupted delivery; the case can be retried.

Processing status and administrator review status are separate. An uploaded case can still be unreviewed.

## Metadata-only evidence

T4, IW5 and T5 Zombies reports remain in the webfront even though no demo can be uploaded. They retain reports, player information, metrics, assignment and review actions. `SendMetadataOnlyCasesToDiscord` controls whether they also generate a Discord message.

## Assignment and review

Moderators can assign a case to themselves, record notes and choose:

- Needs more review;
- Cheating — action taken;
- Cheating — no action taken;
- Not cheating — no action taken;
- Inconclusive.

The original Discord message is updated when assignment or review state changes. Existing attachments remain on the message.

## Clearing reports

Case-scoped report clearing targets only active report penalties linked to the evidence case. It does not clear unrelated reports against the same player.

## Retention

Cases are retained according to both:

- `CaseRetentionDays`;
- `MaxStoredCases`.

The oldest standard cases are pruned first. Cases reviewed as **cheating — action taken** are exempt from both limits and remain stored until an Owner deliberately deletes them. Deleting a retained case does not reverse player penalties or delete its Discord message. See [[Privacy and Data|Privacy-and-Data]] for the retained fields.

Next: [[Demo Matching|Demo-Matching]] or [[Webfront Guide|Webfront-Guide]].
