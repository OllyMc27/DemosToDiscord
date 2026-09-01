# Proactive detection release status

Version `2.4.0` adds the database-driven detector and redesigned Cheating Case Review workspace. Existing evidence cases remain in
`Configuration/DemosToDiscordCases.json`; only a rebuildable compact baseline cache is added.

The current post-2.4 integration branch also accepts administrator-resolved ServerPulse community signals. These are stored and labelled separately from statistical detections. An explicit **Inconclusive** review can optionally apply IW4MAdmin's native `Flagged` level and enable a cooldown-protected Discord alert on a later join.

## Implemented

- Live IW4MAdmin database bootstrap and incremental baseline refresh.
- Conservative empirical percentile scoring with sample/population safeguards.
- Correlated-signal protection and human-review-only outcomes.
- Asynchronous disconnect/match-end evaluation with deduplication.
- Proactive evidence creation/merging, demo routing, and metadata-only routing.
- Separate web case and Discord thresholds with same-message updates.
- Dedicated proactive signal presentation in webfront and Discord.
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
