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

### Evidence triggers and game support

| Setting | Type | Default | Accepted values and behaviour |
|---|---|---|---|
| `UploadOnReports` | Boolean | `true` | Creates or updates a case for a player report. |
| `UploadOnAutomatedBans` | Boolean | `true` | Creates or updates a case for recognised automated anti-cheat bans. |
| `UploadOnManualBans` | Boolean | `false` | Retained for configuration compatibility. Recent manual bans are always offered to an existing case for audit linking; they never create an unrelated case. |
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
| `EnableWebfrontDashboard` | Boolean | `true` | Registers **Admin → Demo Evidence**. |
| `WebfrontMinimumPermission` | Permission name | `Moderator` | Valid IW4MAdmin permission, normally `Moderator`, `Administrator`, `SeniorAdmin` or `Owner`. |
| `StoreReportReasons` | Boolean | `true` | `false` prevents the original report text being retained. |
| `CaseRetentionDays` | Integer | `90` | Cases older than this are pruned. Positive value. |
| `MaxStoredCases` | Integer | `500` | Maximum retained cases; oldest are removed first. Positive value. |
| `ImportLegacyStateFile` | Boolean | `true` | Imports missing cases from the legacy JSON file into `Database.db` once, preserving the source file. |
| `StateFilePath` | String | `Configuration/DemosToDiscordCases.json` | Legacy import and emergency-fallback path; it is no longer the normal case store. |
| `AddPlayerNotesOnReview` | Boolean | `true` | Appends meaningful review outcomes to IW4MAdmin's native player note. |
| `AddPlayerNotesOnAssignment` | Boolean | `false` | Also records case assignment in the native player note; disabled by default to avoid noise. |
| `AddPlayerNotesOnPenalty` | Boolean | `true` | Appends a linked temp/permanent-ban outcome and reason to the native player note. |
| `TimeZone` | String | `Europe/London` | IANA timezone used for webfront and Discord. See [Language and Timezones](Language-and-Timezones). |

### Proactive detection

`ProactiveDetection.Enabled` defaults to `false` so a major upgrade cannot unexpectedly create cases. When enabled, version 3 refreshes its compact baseline in the background and evaluates changed player data after match end or disconnect. It creates human-review cases only and never punishes automatically.

| Setting | Default | Behaviour |
|---|---:|---|
| `Enabled` | `false` | Enables live baseline refresh and proactive review-case creation. |
| `EvaluateOnMatchEnd` | `true` | Queues connected eligible players when a match ends. |
| `EvaluateOnDisconnect` | `true` | Queues an eligible player when IW4MAdmin disposes their session state. |
| `MinimumCaseRiskScore` | `50` | Minimum explainable risk required to create a review case. |
| `MinimumSignalPercentile` | `0.975` | Lowest population percentile allowed to contribute risk. |
| `MinimumTrackedHits` | `100` | Rejects tiny tracked-hit samples. |
| `MinimumPositiveEvents` | `12` | Prevents cases such as two head hits from three events. |
| `MinimumPopulationSize` | `30` | Rejects unstable comparison populations. |
| `FullConfidenceTrackedHits` | `300` | Sample size that receives full signal weight. |
| `FullConfidencePopulationSize` | `100` | Population size that receives full signal weight. |
| `RepeatHistoryDays` | `30` | Window for bounded repeated-outlier weighting. |
| `EvaluationDelaySeconds` | `15` | Delay after match/session events so IW4MAdmin statistics can persist. |
| `BaselineRefreshMinutes` | `5` | Minimum interval between incremental source refreshes. |
| `FullBaselineRebuildHours` | `168` | Periodic full rebuild interval; the default is weekly. |
| `MaximumIncrementalEvents` | `250000` | Maximum source-event batch processed in one incremental refresh. |
| `MaxConcurrentEvaluations` | `1` | Background evaluation workers; keep low on busy SQLite installations. |

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
| `UploadOnManualBans` | Nullable Boolean | Retained for compatibility; recent-case penalty linking is always attempted. |
| `SupportsDemos` | Nullable Boolean | Explicitly enables/disables demo searching. |
| `SendMetadataOnlyCasesToDiscord` | Nullable Boolean | Overrides metadata-only Discord delivery. |
| `ReportRoleId` | String | Overrides the report role ID. |
| `AntiCheatRoleId` | String | Overrides the anti-cheat role ID. |
| `EnableProactiveDetection` | Nullable Boolean | Enables/disables proactive evaluation for the server; use `false` for T5 Zombies. |

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
  "ImportLegacyStateFile": true,
  "StateFilePath": "Configuration/DemosToDiscordCases.json",
  "AddPlayerNotesOnReview": true,
  "AddPlayerNotesOnAssignment": false,
  "AddPlayerNotesOnPenalty": true,
  "ProactiveDetection": {
    "Enabled": false,
    "EvaluateOnMatchEnd": true,
    "EvaluateOnDisconnect": true,
    "MinimumCaseRiskScore": 50,
    "MinimumSignalPercentile": 0.975,
    "MinimumTrackedHits": 100,
    "MinimumPositiveEvents": 12,
    "MinimumPopulationSize": 30,
    "FullConfidenceTrackedHits": 300,
    "FullConfidencePopulationSize": 100,
    "RepeatHistoryDays": 30,
    "EvaluationDelaySeconds": 15,
    "BaselineRefreshMinutes": 5,
    "FullBaselineRebuildHours": 168,
    "MaximumIncrementalEvents": 250000,
    "MaxConcurrentEvaluations": 1
  },
  "TimeZone": "Europe/London",
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
      "EnableProactiveDetection": true,
      "SendMetadataOnlyCasesToDiscord": true,
      "ReportRoleId": "",
      "AntiCheatRoleId": ""
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

### Temporary demo diagnostics

```json
"Debug": true
```

Restart, reproduce one case, save the relevant log lines, then return `Debug` to `false`.

## JSON rules and common mistakes

- Use double quotes around properties and string values.
- Escape Windows path separators as `\\`.
- Do not leave a comma after the final property.
- Keep game codes consistently uppercase for readability.
- Never expose a complete webhook URL publicly.
- Restart the whole IW4MAdmin process after changes.

Next: [[Language and Timezones|Language-and-Timezones]], [[Discord Integration|Discord-Integration]] or [[Troubleshooting]].
