# Proactive detection preview status

Version `2.4.0-preview.1` adds the database-driven core detector on the
`feature/proactive-detection` branch. Existing evidence cases remain in
`Configuration/DemosToDiscordCases.json`; only a rebuildable compact baseline cache is added.

## Implemented

- Live IW4MAdmin database bootstrap and incremental baseline refresh.
- Conservative empirical percentile scoring with sample/population safeguards.
- Correlated-signal protection and human-review-only outcomes.
- Asynchronous disconnect/match-end evaluation with deduplication.
- Proactive evidence creation/merging, demo routing, and metadata-only routing.
- Separate web case and Discord thresholds with same-message updates.

## Current limitations

- No automatic punishment is implemented by design.
- T5 Zombies is excluded because its population is not comparable to multiplayer.
- Exact accuracy is unavailable because shots fired is not persisted.
- The current preview has no dedicated Review Board UI; cases use the existing evidence workflow.
- Live server validation and threshold observation are required before a stable release.

## Future game-side telemetry

The current T6 GSC already watches `+attack`, records the time since the last attack, captures
view-angle history, and emits weapon/hit-location context for damage and kills. A future,
separate telemetry stage could safely add aggregate shots fired per player/weapon/session and
stable map/mode/session identifiers. That would enable genuine hits/shots accuracy. Richer
wallbang or visibility context should only be added where the engine can report it reliably;
the detector must not infer these values from the current hit data.

## Next stage after live validation

Add the Proactive Review Board UI and expose the point-in-time signal breakdown on the existing
case detail page. Player-note integration and game-script telemetry remain later stages.
