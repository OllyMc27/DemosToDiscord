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
| `EnableWebfrontDashboard` | Boolean | `true` | Registers **Admin → Demo Evidence**. |
| `WebfrontMinimumPermission` | Permission name | `Moderator` | Valid IW4MAdmin permission, normally `Moderator`, `Administrator`, `SeniorAdmin` or `Owner`. |
| `StoreReportReasons` | Boolean | `true` | `false` prevents the original report text being retained. |
| `CaseRetentionDays` | Integer | `90` | Cases older than this are pruned. Positive value. |
| `MaxStoredCases` | Integer | `500` | Maximum retained cases; oldest are removed first. Positive value. |
| `StateFilePath` | String | `Configuration/DemosToDiscordCases.json` | Absolute path or path relative to IW4MAdmin. |
| `TimeZone` | String | `Europe/London` | IANA timezone used for webfront and Discord. See [Language and Timezones](Language-and-Timezones). |

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
