# Configuration

DemosToDiscord reads `Configuration/DemosToDiscord.json`. Restart IW4MAdmin after changing it so the background workers use the new values.

The [repository example](https://github.com/OllyMc27/DemosToDiscord/blob/master/examples/DemosToDiscord.json) is safe to copy after replacing its placeholder paths and webhook URLs.

## Minimal configuration

```json
{
  "Enabled": true,
  "Webhook": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN",
  "T5DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t6\\demos",
  "TimeZone": "Europe/London",
  "UploadOnReports": true,
  "UploadOnAutomatedBans": true,
  "EnableWebfrontDashboard": true
}
```

## Every top-level option

### General

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `Enabled` | Boolean | `true` | Enables evidence collection. `false` leaves the plugin loaded but ignores new penalty events. |
| `Webhook` | String | Empty | Complete HTTPS Discord webhook used when no game/server webhook overrides it. |
| `Debug` | Boolean | `false` | Logs detailed candidate reasons and tests the default webhook during startup. Use temporarily. |

### Demo folders

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `T5DemoPath` | String | Plutonium T5 path | Absolute folder containing T5 multiplayer `.demo` files. |
| `T6DemoPath` | String | Plutonium T6 path | Absolute folder containing T6 `.demo` and `.json` files. |

The paths must belong to the Windows account that records the demos. A service account running IW4MAdmin must have read permission.

Windows displays a path with one backslash:

```text
C:\Users\Administrator\AppData\Local\Plutonium\storage\t6\demos
```

JSON uses the backslash as an escape character, so **every backslash must be doubled** in `DemosToDiscord.json`:

```json
{
  "T5DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t6\\demos"
}
```

Do not mix single and doubled backslashes. Everything before `Plutonium` can differ with the Windows account or installation, but the normal remainder is `storage\t5\demos` or `storage\t6\demos`.

#### Confirm that Plutonium is producing demos

DemosToDiscord reads completed files; it does not turn game recording on itself. Before testing the plugin:

1. Keep Plutonium updated.
2. Complete a T5 or T6 multiplayer match with at least one real player connected.
3. Confirm a new `.demo` appears in the relevant folder. T6 should also produce a matching `.json` sidecar.
4. Only then copy that folder into `T5DemoPath` or `T6DemoPath`, using doubled backslashes in JSON.

[Current Plutonium multiplayer builds automatically record while players are connected](https://plutonium.pw/docs/changelog/). Do not rely on adding `demo_enabled 1` to a T6 `server.cfg`: [Plutonium documents that dvar as non-functional](https://plutonium.pw/docs/server/dvars/). If the match produces no file, fix Plutonium recording/storage first; the plugin cannot upload a demo that was never written.

### Evidence triggers and game support

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `UploadOnReports` | Boolean | `true` | Creates or updates a case for a player report. |
| `UploadOnAutomatedBans` | Boolean | `true` | Creates or updates a case for recognised automated anti-cheat bans. |
| `UploadOnManualBans` | Boolean | `false` | Observes manual bans. A ban only links to a recent case; it does not create an unrelated case. |
| `AutomatedBanGames` | String array | `[ "T6" ]` | Game codes allowed to trigger automated-ban evidence. |
| `SupportedDemoGames` | String array | `[ "T5", "T6" ]` | Games expected to produce downloadable demos. Others remain metadata-only. |
| `T5ZombieMapPrefixes` | String array | `[ "zombie_" ]` | Map prefixes identifying T5 Zombies as demo-unsupported. |
| `T5ZombieModes` | String array | `[ "zclassic", "zstandard", "zombie" ]` | Mode codes identifying T5 Zombies as demo-unsupported. |

### Search and queue timing

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---:|---|
| `MaxLookbackMinutes` | Integer | `90` | Oldest demo start considered before the evidence event. Positive value. |
| `MaxWaitMinutes` | Integer | `30` | Maximum wait for a demo to appear and become readable. Positive value. |
| `RetryIntervalSeconds` | Integer | `10` | Delay between search/readiness attempts. Positive value. |
| `PostMatchDelaySeconds` | Integer | `10` | Extra delay before upload after the file is stable. `0` disables it. |
| `FileStableChecks` | Integer | `3` | Unchanged file-size checks required before upload. Positive value. |
| `MaxConcurrentUploads` | Integer | `2` | Evidence workers. Use at least `1`; keep low to avoid upload bursts. |
| `DeduplicationWindowMinutes` | Integer | `120` | Groups evidence for the same player, server, map and mode. |

### Webfront and retained cases

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `EnableWebfrontDashboard` | Boolean | `true` | Registers **Admin → Cheating Case Review**. |
| `WebfrontMinimumPermission` | Permission name | `Moderator` | Valid IW4MAdmin permission, normally `Moderator`, `Administrator`, `SeniorAdmin` or `Owner`. |
| `StoreReportReasons` | Boolean | `true` | `false` prevents the original report text being retained. |
| `CaseRetentionDays` | Integer | `90` | Standard cases older than this are pruned. **Cheating — action taken** cases are permanent unless an Owner deletes them. Positive value. |
| `MaxStoredCases` | Integer | `500` | Target collection size. Oldest non-permanent cases are removed first; protected confirmed-cheating cases can make the total exceed this value. Positive value. |
| `StateFilePath` | String | `Configuration/DemosToDiscordCases.json` | Absolute path or path relative to IW4MAdmin. |
| `TimeZone` | String | `Europe/London` | IANA timezone used for webfront and Discord. See [Language and Timezones](Language-and-Timezones). |

### Proactive statistical review

These settings control human-review suggestions built from IW4MAdmin's existing statistics. They never enable automatic punishment. See [[Proactive Detection|Proactive-Detection]] before tuning thresholds.

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---:|---|
| `EnableProactiveDetection` | Boolean | `true` | Evaluates eligible real-player sessions after disconnect/match end. |
| `ProactiveBaselineRefreshMinutes` | Integer | `5` | Minutes between live database baseline refreshes. Positive value. |
| `ProactiveBaselineStateFilePath` | String | `Configuration/DemosToDiscordProactiveBaselines.json` | Rebuildable aggregate baseline cache path. |
| `ProactiveMinimumPopulation` | Integer | `100` | Minimum comparable player/server population before scoring. |
| `ProactiveMinimumTrackedHits` | Integer | `200` | Player tracked-hit sample required for hit-location/mechanics signals. |
| `ProactiveMinimumHeadEvents` | Integer | `10` | Minimum player head events required before head-rate signals contribute. |
| `ProactiveExcludedGames` | String array | `[]` | Game codes never evaluated, for example `[ "T5" ]`. |
| `ProactiveExcludedServerIds` | Integer array | `[]` | IW4MAdmin server IDs never evaluated. |
| `ProactiveExcludeT5Zombies` | Boolean | `true` | Keeps the non-comparable Zombies population out of proactive review. |
| `ProactiveCaseRiskThreshold` | Integer | `50` | Minimum 0–100 score retained as a case. Recommended production default: `50`. |
| `ProactiveDiscordRiskThreshold` | Integer | `65` | Minimum score posted to Discord when notifications are enabled. |
| `EnableProactiveDiscordNotifications` | Boolean | `true` | Sends qualifying retained proactive cases to Discord. |
| `ProactiveRepeatHistoryWeight` | Integer | `4` | Maximum extra weight contributed by earlier proactive case history. |
| `ProactiveEvaluationDelaySeconds` | Integer | `20` | Wait after session completion before reading final statistics. |
| `ProactiveEvaluationDeduplicationMinutes` | Integer | `30` | Suppresses repeated evaluation of the same player/server session window. |
| `ProactiveEvaluationQueueCapacity` | Integer | `1000` | Maximum pending background evaluations; keep comfortably above peak disconnect volume. |

### ServerPulse handoff and flagged-player live review

ServerPulse community signals are accepted only after an administrator resolves the accused player. They are labelled separately from statistical detections and never constitute proof by themselves.

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---:|---|
| `AcceptServerPulseCases` | Boolean | `true` | Accepts explicit administrator-resolved Player Guidance handoffs from a compatible ServerPulse plugin. |
| `FlagPlayerOnInconclusiveReview` | Boolean | `true` | Uses IW4MAdmin's native Flag event when an administrator closes a case as **Inconclusive**. Statistical detection alone cannot call this action. |
| `NotifyDiscordWhenFlaggedPlayerJoins` | Boolean | `true` | Sends a live-review alert when any IW4MAdmin `Flagged` player joins a monitored server. |
| `FlaggedPlayerJoinAlertCooldownMinutes` | Integer | `15` | Per-player Discord alert cooldown; clamped to 1–1,440 minutes. |
| `FlaggedPlayerRoleId` | String | Empty | Numeric Discord role ID mentioned by flagged-player join alerts. Empty disables the mention. |

### Discord delivery

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `SendMetadataOnlyCasesToDiscord` | Boolean | `true` | Sends unsupported-game/mode cases without a demo attachment. |
| `ReportRoleId` | String | Empty | Numeric Discord role ID for report evidence. Empty disables the mention. |
| `AntiCheatRoleId` | String | Empty | Numeric Discord role ID for anti-cheat evidence. Empty disables the mention. |
| `MentionRolesOnlyWhenDemoReady` | Boolean | `false` | Suppresses role mentions until a demo is attached when `true`. |
| `GameWebhooks` | Object | Empty | Maps game codes such as `T5`/`T6` to webhook URLs. |
| `ServerOverrides` | Object | Empty | Maps an endpoint, legacy server ID or `*` to server-specific settings. |

## Every server-override option

| Setting | Type | Behaviour |
|---|---|---|
| `Enabled` | Nullable Boolean | `false` disables collection for this server; omitted inherits globally. |
| `DemoPath` | String | Replaces the game's demo path for this server. |
| `Webhook` | String | Highest-priority webhook for this server. |
| `UploadOnReports` | Nullable Boolean | Overrides report collection. |
| `UploadOnAutomatedBans` | Nullable Boolean | Overrides automated-ban collection. |
| `UploadOnManualBans` | Nullable Boolean | Overrides manual-ban observation. |
| `SupportsDemos` | Nullable Boolean | Explicitly enables/disables demo searching. |
| `SendMetadataOnlyCasesToDiscord` | Nullable Boolean | Overrides metadata-only Discord delivery. |
| `ReportRoleId` | String | Overrides the report role ID. |
| `AntiCheatRoleId` | String | Overrides the anti-cheat role ID. |
| `AcceptServerPulseCases` | Nullable Boolean | Enables/disables manual ServerPulse case handoff for this server. |
| `FlaggedPlayerRoleId` | String | Overrides the flagged-player live-review role ID. |

Server override selection: exact endpoint → legacy server ID → `"*"` fallback → global settings.

Webhook selection: server override → `GameWebhooks` → default `Webhook`.

## Complete example

```json
{
  "Enabled": true,
  "Webhook": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN",
  "T5DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t6\\demos",
  "UploadOnReports": true,
  "UploadOnAutomatedBans": true,
  "UploadOnManualBans": false,
  "AutomatedBanGames": [ "T6" ],
  "SupportedDemoGames": [ "T5", "T6" ],
  "T5ZombieMapPrefixes": [ "zombie_" ],
  "T5ZombieModes": [ "zclassic", "zstandard", "zombie" ],
  "MaxLookbackMinutes": 90,
  "MaxWaitMinutes": 30,
  "RetryIntervalSeconds": 10,
  "PostMatchDelaySeconds": 10,
  "FileStableChecks": 3,
  "MaxConcurrentUploads": 2,
  "DeduplicationWindowMinutes": 120,
  "EnableWebfrontDashboard": true,
  "WebfrontMinimumPermission": "Moderator",
  "StoreReportReasons": true,
  "CaseRetentionDays": 90,
  "MaxStoredCases": 500,
  "StateFilePath": "Configuration/DemosToDiscordCases.json",
  "TimeZone": "Europe/London",
  "EnableProactiveDetection": true,
  "ProactiveBaselineRefreshMinutes": 5,
  "ProactiveBaselineStateFilePath": "Configuration/DemosToDiscordProactiveBaselines.json",
  "ProactiveMinimumPopulation": 100,
  "ProactiveMinimumTrackedHits": 200,
  "ProactiveMinimumHeadEvents": 10,
  "ProactiveExcludedGames": [],
  "ProactiveExcludedServerIds": [],
  "ProactiveExcludeT5Zombies": true,
  "ProactiveCaseRiskThreshold": 50,
  "ProactiveDiscordRiskThreshold": 65,
  "EnableProactiveDiscordNotifications": true,
  "ProactiveRepeatHistoryWeight": 4,
  "ProactiveEvaluationDelaySeconds": 20,
  "ProactiveEvaluationDeduplicationMinutes": 30,
  "ProactiveEvaluationQueueCapacity": 1000,
  "AcceptServerPulseCases": true,
  "FlagPlayerOnInconclusiveReview": true,
  "NotifyDiscordWhenFlaggedPlayerJoins": true,
  "FlaggedPlayerJoinAlertCooldownMinutes": 15,
  "FlaggedPlayerRoleId": "",
  "SendMetadataOnlyCasesToDiscord": true,
  "ReportRoleId": "",
  "AntiCheatRoleId": "",
  "MentionRolesOnlyWhenDemoReady": false,
  "GameWebhooks": {
    "T5": "",
    "T6": ""
  },
  "Debug": false,
  "ServerOverrides": {
    "127.0.0.1:4976": {
      "DemoPath": "D:\\Plutonium\\storage\\t6\\demos",
      "Webhook": "https://discord.com/api/webhooks/OPTIONAL_SERVER_WEBHOOK",
      "SupportsDemos": true,
      "SendMetadataOnlyCasesToDiscord": true,
      "ReportRoleId": "",
      "AntiCheatRoleId": "",
      "AcceptServerPulseCases": true,
      "FlaggedPlayerRoleId": ""
    }
  }
}
```

## Practical examples

### Separate T5 and T6 Discord channels

```json
"GameWebhooks": {
  "T5": "https://discord.com/api/webhooks/T5_ID/T5_TOKEN",
  "T6": "https://discord.com/api/webhooks/T6_ID/T6_TOKEN"
}
```

### One server with a different folder and webhook

```json
"ServerOverrides": {
  "127.0.0.1:4976": {
    "DemoPath": "D:\\Plutonium\\storage\\t6\\demos",
    "Webhook": "https://discord.com/api/webhooks/SERVER_ID/SERVER_TOKEN",
    "SupportsDemos": true
  }
}
```

### Disable one server

```json
"ServerOverrides": {
  "127.0.0.1:28960": {
    "Enabled": false
  }
}
```

### Keep no-demo cases off Discord

```json
"SendMetadataOnlyCasesToDiscord": false
```

### Mention separate report and anti-cheat roles

```json
"ReportRoleId": "123456789012345678",
"AntiCheatRoleId": "234567890123456789",
"MentionRolesOnlyWhenDemoReady": true
```

Use numeric role IDs, not `@Role Name`. The webhook must be allowed to mention the role.

### Reduce retained report data

```json
"StoreReportReasons": false,
"CaseRetentionDays": 30,
"MaxStoredCases": 200
```

### Collect reports only

```json
"UploadOnReports": true,
"UploadOnAutomatedBans": false,
"UploadOnManualBans": false
```

To disable proactive cases as well, also set `"EnableProactiveDetection": false`.

### Exclude a game or server from proactive review

```json
"ProactiveExcludedGames": [ "T5" ],
"ProactiveExcludedServerIds": [ 12, 27 ]
```

Server IDs are IW4MAdmin's numeric server database IDs, not endpoint ports.

### Quiet proactive Discord without disabling web cases

```json
"EnableProactiveDetection": true,
"EnableProactiveDiscordNotifications": false
```

### Temporary demo diagnostics

```json
"Debug": true
```

Restart, reproduce one case, save the relevant log lines, then return `Debug` to `false`.

## JSON rules and common mistakes

- Use double quotes around properties and string values.
- Escape every Windows path separator as `\\`: an Explorer path such as `C:\Users\Administrator\...` becomes `"C:\\Users\\Administrator\\..."` in JSON.
- Do not mix escaped and unescaped separators in the same path.
- Do not leave a comma after the final property.
- Keep game codes consistently uppercase for readability.
- Never expose a complete webhook URL publicly.
- Restart the whole IW4MAdmin process after changes.

Next: [[Language and Timezones|Language-and-Timezones]], [[Discord Integration|Discord-Integration]] or [[Troubleshooting]].
