# DemosToDiscord 3.0 development status

Checkpoint date: 30 August 2026

## Current state

The proactive human-review upgrade is implemented as one coherent release candidate. It builds cleanly and its executable verification suite passes. Proactive detection is disabled by default and never punishes a player automatically.

## Finished

- Versioned SQLite persistence for cases, reports, audit events and point-in-time detection signals.
- Transactional migration 1 for evidence storage and migration 2 for proactive baseline/evaluation state.
- Safe one-time import of the legacy JSON case store, preserving the source file and retaining an emergency fallback.
- Native IW4MAdmin player-note integration for meaningful review and linked penalty outcomes.
- Manual bans link to a recent case and retain penalty/note identifiers instead of creating an unrelated case.
- Conservative, explainable risk scoring with sample, positive-event and population minimums.
- T6/IW5 weapon-specific tracked-hit head-rate evaluation with attachment normalization.
- T4/T5 multiplayer cumulative head-rate fallback and explicit T5 Zombies exclusion.
- Compact full/incremental `EFClientKills` baseline cache with high-water tracking, telemetry-quality counters and periodic full rebuilds.
- Delayed, deduplicated match-end and disconnect scheduling with unchanged-data suppression.
- Proactive case create/merge, existing demo routing, Discord synchronization and case audit history.
- Dedicated global/per-server proactive Discord role routing; proactive-only cases do not borrow the report role.
- Dedicated Proactive Review navigation, baseline-health banner, minimum-risk filter, risk badges and signal explanations.
- `!dtdbaseline` health and `!dtdrebuildbaseline` maintenance commands.
- Configuration, installation, command, README and changelog updates for version 3.0.0.

## Architecture

- `DemosToDiscordDatabase` owns plugin migrations and normalized case persistence inside IW4MAdmin's configured `Database.db`.
- `ProactiveBaselineService` builds and incrementally refreshes compact population members, calculates empirical percentiles and records evaluation high-water state.
- `ProactiveDetectionService` receives match/disconnect events, coalesces duplicate requests, waits for stats persistence and orchestrates evaluation.
- `RiskScorer` converts eligible observations into bounded explainable risk. Repeat history is capped and cannot create risk by itself.
- `DemoUploadService.CaptureProactiveAsync` merges qualifying evidence into the normal case/demo/Discord workflow.
- The webfront and Discord clients render the persisted signal explanation; they do not recalculate risk while viewing a case.

## Database schema

Migration 1:

- `DemosToDiscordCases`
- `DemosToDiscordCaseReports`
- `DemosToDiscordCaseEvents`
- `DemosToDiscordDetectionSignals`
- `DemosToDiscordSchemaMigrations`

Migration 2:

- `DemosToDiscordBaselineState`: source high-water, rebuild/refresh times, counts, quality and last error.
- `DemosToDiscordBaselineMembers`: game/server/player/raw-weapon aggregate counts and last source event.
- `DemosToDiscordEvaluationState`: per server/player evaluated source anchors, time, score and outcome.

The plugin schema currently requires IW4MAdmin's SQLite provider. No IW4MAdmin-owned table is altered.

## Data decisions

- Exact conventional accuracy is unavailable because shots fired, including misses, are not persisted.
- `Map`, `VisibilityPercentage` and `Fraction` were effectively empty in the supplied tracked-event dataset, so map, visibility and wallbang signals remain disabled.
- Anti-cheat snapshots are enforcement-selected and are displayed as case context, not used as the population baseline.
- T5 Zombies statistics are not comparable to multiplayer and are excluded from proactive scoring.
- Statistical risk creates a review candidate, not a cheat verdict or punishment.

## Verification

Last verified command sequence:

```text
dotnet build DemosToDiscord.sln -c Release --no-restore -p:NuGetAudit=false
dotnet run --project DemosToDiscord.Tests/DemosToDiscord.Tests.csproj -c Release --no-build --no-restore
```

Result: Release build succeeded with 0 warnings and 0 errors; 8/8 checks passed.

Coverage includes migrations, case graph round-trip/cascade, legacy import/merge, proactive/report case merging, Discord role priority, risk safeguards, 40,800-event baseline rebuild, incremental high-water processing, attachment normalization, T6 live population evaluation, unchanged-data suppression, T5 fallback, game capability exclusions and native note preservation.

## Remaining before production rollout

1. Deploy to a staging IW4MAdmin instance using a copy of the production database.
2. Leave proactive detection disabled for the first start and verify migrations plus `!dtdbaseline`.
3. Enable it during a quiet period and time the first full baseline build on the host hardware.
4. Run several real T6, IW5, T4 and T5 matches and confirm event ordering, demo matching and Discord delivery.
5. Review early candidates for false-positive behaviour before changing thresholds.
6. Tag and publish `v3.0.0` only after that soak test.

## Known limitations / future work

- SQLite is the only supported provider for plugin-owned schema and baseline SQL.
- Enabling proactive detection after startup requires an IW4MAdmin restart.
- Exact accuracy needs new shots-fired telemetry and game-specific burst/projectile validation.
- Map-aware and visibility/wallbang scoring must remain off until collection is demonstrably populated.
- A future UI action could trigger baseline rebuild directly; the current safe maintenance action is the SeniorAdmin command.
- Production telemetry should be observed before adding any additional independent metric to the risk score.

## Next exact steps

1. Copy the release DLL and configuration to a staging IW4MAdmin installation.
2. Back up `Database.db`, including WAL/SHM files after stopping IW4MAdmin.
3. Start with `ProactiveDetection.Enabled=false`; confirm schema migration 2 and normal evidence workflow.
4. Set `ProactiveDetection.Enabled=true`, restart, run `!dtdbaseline`, and inspect the Proactive Review page.
5. If the baseline reports an error, capture the complete IW4MAdmin log before changing code.
