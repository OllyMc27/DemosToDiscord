# Evidence review release status

The current unreleased change reuses the population scorer as read-only review context on every evidence case. Ordinary report, anti-cheat and ServerPulse cases retain their original source and outcome; the live comparison never creates, upgrades or punishes from the case page.

Version `2.5.1` removes the separate proactive Discord threshold. Every retained proactive case now enters one demo-or-metadata Discord workflow, with Moderator-or-higher evidence retries updating the original message.

Version `2.5.0` adds administrator-resolved ServerPulse community signals, optional native flagging after an explicit **Inconclusive** review, and cooldown-protected Discord alerts when flagged players later join.

Version `2.4.0` introduced the database-driven detector and redesigned Cheating Case Review workspace. Existing evidence cases remain in
`Configuration/DemosToDiscordCases.json`; only a rebuildable compact baseline cache is added.

ServerPulse signals are stored and labelled separately from statistical detections. Statistical and chat detections remain human-review-only.

## Implemented

- Live IW4MAdmin database bootstrap and incremental baseline refresh.
- Conservative empirical percentile scoring with sample/population safeguards.
- Correlated-signal protection and human-review-only outcomes.
- Asynchronous disconnect/match-end evaluation with deduplication.
- Proactive evidence creation/merging, demo routing, and metadata-only routing.
- One proactive case threshold with automatic demo-or-metadata Discord routing and same-message updates.
- Dedicated proactive signal presentation in webfront and Discord.
- Read-only current baseline comparisons for all webfront evidence cases.
- Compact queue-first review workspace, friendly map/mode display, Owner deletion and permanent confirmed-cheating retention.

## Current limitations

- Statistical and chat detection never punish automatically. Native flagging occurs only after an administrator explicitly records an **Inconclusive** case decision and the option is enabled.
- T5 Zombies is excluded because its population is not comparable to multiplayer.
- Exact accuracy is unavailable because shots fired is not persisted.
- The detector uses the existing shared case workflow rather than a separate review board.
- Exact thresholds should still be observed against each community's real population before custom tuning.

## Future game-side telemetry

The current T6 GSC already watches `+attack`, records the time since the last attack, captures
view-angle history, and emits weapon/hit-location context for damage and kills. A future,
separate telemetry stage could safely add aggregate shots fired per player/weapon/session and
stable map/mode/session identifiers. That would enable genuine hits/shots accuracy. Richer
wallbang or visibility context should only be added where the engine can report it reliably;
the detector must not infer these values from the current hit data.

## Later stages

Player-note integration and game-script telemetry remain future work. They are not required by the database-driven 2.4 detector.
