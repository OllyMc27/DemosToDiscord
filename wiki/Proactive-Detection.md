# Proactive Detection

DemosToDiscord 2.4 can identify statistically unusual real-player sessions even when nobody submits a report. It uses the live statistics IW4MAdmin already stores, creates an explainable case for a moderator, and then uses the same demo, Discord and review workflow as reported evidence.

It is a review aid, not an automatic anti-cheat verdict. The detector never bans, kicks, flags, clears reports or changes a player's standing.

## How it works

1. The plugin refreshes compact, server-aware population baselines from IW4MAdmin's database.
2. A real player's disconnect or match end queues an evaluation after the configured delay.
3. The player's eligible statistics are compared with comparable players, preferring the same server and falling back to the game-wide population when necessary.
4. Conservative empirical percentiles, sample safeguards and correlated-signal grouping produce a 0–100 risk score.
5. A case is retained only when it reaches `ProactiveCaseRiskThreshold`; Discord uses its own, normally higher threshold.
6. A moderator reviews the explained indicators, demo and surrounding evidence before deciding what to do.

## Signals currently used

- Kills/deaths, score per minute and IW4MAdmin performance.
- Tracked-hit head rate when the player has enough tracked hits and head events.
- T6/IW5 killing-hit head rate, maximum strain and average snap where those statistics are available.
- Earlier retained proactive history as a small, capped contextual factor.

Related measurements are grouped so one underlying behaviour is not counted several times. A high value is not proof of cheating: population, game style, weapons, bots and unusual legitimate sessions can all affect statistics.

## Supported sessions

| Game/session | Proactive statistics | Demo when a case is retained |
|---|---|---|
| T6 multiplayer | Full supported set | Yes |
| IW5 multiplayer | Full supported set | Metadata only |
| T5 multiplayer | Core and tracked-hit signals | Yes |
| T4 multiplayer | Core and tracked-hit signals | Metadata only |
| T5 Zombies | Excluded by default | Metadata only if enabled |

Bots are never evaluated as suspects. IW4MAdmin's `IgnoreBots` setting may affect which combat statistics reach its database, so review bot-heavy servers separately and do not treat those populations as interchangeable with public PvP servers.

## Risk levels and thresholds

The presentation bands are Normal (`0–24`), Elevated (`25–49`), Review (`50–64`), High (`65–79`) and Very high (`80–100`). The defaults retain cases at `50` and notify Discord at `65`.

Do not lower production thresholds merely to prove the feature works: doing so intentionally creates normal/low-value cases. Verify startup baseline logs first, then observe real traffic with the defaults.

```json
"EnableProactiveDetection": true,
"ProactiveMinimumPopulation": 100,
"ProactiveMinimumTrackedHits": 200,
"ProactiveMinimumHeadEvents": 10,
"ProactiveCaseRiskThreshold": 50,
"ProactiveDiscordRiskThreshold": 65,
"EnableProactiveDiscordNotifications": true
```

Every proactive option and exclusion is listed in [[Configuration]].

## What moderators see

A proactive case names each indicator that crossed the statistical floor, shows its observed value and comparable percentile, and explains why it contributed. The case also includes available match evidence, friendly and raw map/mode names, player metrics, assignments, notes and the normal decision controls.

If no indicator qualifies, there is no useful signal breakdown to display and, at production thresholds, no case is retained. Existing report/ban cases do not gain a retrospective proactive assessment.

## Confirm that it is running

After startup, look for a successful proactive baseline refresh in the IW4MAdmin log. A healthy refresh reports non-zero player/server and weapon population counts. A newly installed plugin may need several minutes and completed player sessions before the first eligible evaluation.

Set `Debug` to `true` temporarily when diagnosing exclusions or insufficient samples, reproduce one completed session, preserve the relevant log lines, then return it to `false`.

## Baseline cache and privacy

`Configuration/DemosToDiscordProactiveBaselines.json` is a compact, rebuildable aggregate cache. It is not a copy of demos or chat. Deleting it while IW4MAdmin is stopped forces a rebuild from the live database on the next start. See [[Privacy and Data|Privacy-and-Data]].

## Future game-side telemetry

The current detector deliberately stops at statistics IW4MAdmin already stores. A later, separate game-script stage could extend T6's existing `+attack` tracking to aggregate weapon shots, allowing genuine hits/shots accuracy instead of inference from hit data. Stable session identity, weapon-specific shot counts, richer hit context and technically reliable visibility/wallbang context are also possible future inputs. None of those game-script changes are part of 2.4.

Next: [[Evidence Workflow|Evidence-Workflow]], [[Webfront Guide|Webfront-Guide]] or [[Troubleshooting]].
