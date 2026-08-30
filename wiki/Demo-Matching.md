# Demo Matching

DemosToDiscord keeps the original Plutonium filenames. It does not rename `.demo` or `.json` files, because renaming can prevent downloaded evidence appearing in theatre.

## Filename information

Plutonium filenames encode the mode, map and local match start time. Examples:

```text
koth_mp_raid_8_29_2026_23_23.demo
hq_mp_hijacked_8_29_2026_22_56.demo
```

The plugin converts the filename time to UTC before comparing it with the evidence event.

## Search window

A candidate must fall between:

- the evidence time minus `MaxLookbackMinutes`;
- the evidence time plus a small allowance.

It must also match the recorded map. The closest and strongest candidate receives the highest score.

## T6 matching

T6 selection considers:

1. map and search window;
2. the target network ID in the `.json` metadata;
3. mode agreement;
4. distance between match start and report/detection time.

Target GUID confirmation from the JSON sidecar has the highest score.

### Why T6 mode is a preference

IW4MAdmin's live mode and the completed demo filename can briefly disagree around rotations or map changes. A correct-map, correct-time, GUID-confirmed demo is therefore not rejected only because the mode differs.

Examples of T6 codes:

| Code | Mode |
|---|---|
| `dm` | Free-for-All |
| `war` | Team Deathmatch |
| `koth` | Hardpoint |
| `hq` | Headquarters |
| `dom` | Domination |
| `sd` | Search and Destroy |
| `dem` | Demolition |
| `conf` | Kill Confirmed |
| `ctf` | Capture the Flag |

`koth` is Hardpoint; `hq` is Headquarters.

## T5 matching

T5 multiplayer uses strict map, mode and time matching. T5 Zombies maps/modes are treated as unsupported evidence because the required multiplayer demo recording is unavailable.

## File readiness

Finding a filename is not enough. The plugin waits until:

- file size stops changing for `FileStableChecks` checks;
- the file can be opened without another process locking it;
- the optional post-match delay has elapsed.

## Debugging a mismatch

Temporarily enable:

```json
"Debug": true
```

The log then records each scanned file and a result such as:

- `filename-unparsed`;
- `outside-lookback`;
- `after-event`;
- `map-mismatch`;
- `mode-mismatch`;
- `candidate-mode-fallback`;
- `candidate`.

Use `!dtdfind <case-id>` to preview the current best candidate. Return `Debug` to `false` after collecting the required diagnostics.

Next: [[Troubleshooting]] or [[Evidence Workflow|Evidence-Workflow]].
