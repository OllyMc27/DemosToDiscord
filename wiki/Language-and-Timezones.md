# Language and Timezones

## Display language

DemosToDiscord 2.3.x does **not** currently have a `Language` configuration option.

- Plugin-owned webfront labels are English.
- Plugin-owned Discord messages and embeds are English.
- Admin command names are English.
- Generic IW4MAdmin interaction and permission messages can still follow IW4MAdmin's translation system.

Do not add a `"Language"` property to `DemosToDiscord.json`; this version will not use it. A future translation feature would require translated message resources and a new documented setting.

## Timestamp format

The plugin displays dates as:

```text
HH:mm:ss dd/MM/yyyy
```

Example:

```text
23:28:21 29/08/2026
```

`TimeZone` changes the local time represented. It does not change the date format or display language.

## Timezone setting

Default:

```json
"TimeZone": "Europe/London"
```

`Europe/London` automatically changes between GMT and BST. An invalid identifier produces a warning and falls back to `Europe/London`.

Use an IANA timezone identifier. The operating system and .NET runtime determine the complete list, so hundreds of identifiers can be valid. Common choices are below.

### Universal and UK

| Location | Value |
|---|---|
| Coordinated Universal Time | `UTC` |
| United Kingdom | `Europe/London` |
| Ireland | `Europe/Dublin` |

### Europe

| Location | Value | Location | Value |
|---|---|---|---|
| Amsterdam | `Europe/Amsterdam` | Athens | `Europe/Athens` |
| Berlin | `Europe/Berlin` | Brussels | `Europe/Brussels` |
| Bucharest | `Europe/Bucharest` | Copenhagen | `Europe/Copenhagen` |
| Helsinki | `Europe/Helsinki` | Lisbon | `Europe/Lisbon` |
| Madrid | `Europe/Madrid` | Moscow | `Europe/Moscow` |
| Oslo | `Europe/Oslo` | Paris | `Europe/Paris` |
| Prague | `Europe/Prague` | Rome | `Europe/Rome` |
| Stockholm | `Europe/Stockholm` | Vienna | `Europe/Vienna` |
| Warsaw | `Europe/Warsaw` | Zurich | `Europe/Zurich` |

### North America

| Location | Value |
|---|---|
| US/Canada Eastern | `America/New_York` |
| US/Canada Central | `America/Chicago` |
| US/Canada Mountain | `America/Denver` |
| US/Canada Pacific | `America/Los_Angeles` |
| Alaska | `America/Anchorage` |
| Arizona | `America/Phoenix` |
| Atlantic Canada | `America/Halifax` |
| Newfoundland | `America/St_Johns` |
| Hawaii | `Pacific/Honolulu` |
| Mexico City | `America/Mexico_City` |
| Toronto | `America/Toronto` |
| Vancouver | `America/Vancouver` |

### South America

| Location | Value |
|---|---|
| Buenos Aires | `America/Argentina/Buenos_Aires` |
| Bogotá | `America/Bogota` |
| Lima | `America/Lima` |
| Santiago | `America/Santiago` |
| São Paulo | `America/Sao_Paulo` |

### Asia and Middle East

| Location | Value | Location | Value |
|---|---|---|---|
| Bangkok | `Asia/Bangkok` | Dubai | `Asia/Dubai` |
| Hong Kong | `Asia/Hong_Kong` | Jerusalem | `Asia/Jerusalem` |
| Kolkata | `Asia/Kolkata` | Manila | `Asia/Manila` |
| Seoul | `Asia/Seoul` | Shanghai | `Asia/Shanghai` |
| Singapore | `Asia/Singapore` | Taipei | `Asia/Taipei` |
| Tokyo | `Asia/Tokyo` |  |  |

### Australia, New Zealand and Pacific

| Location | Value |
|---|---|
| Adelaide | `Australia/Adelaide` |
| Brisbane | `Australia/Brisbane` |
| Darwin | `Australia/Darwin` |
| Melbourne | `Australia/Melbourne` |
| Perth | `Australia/Perth` |
| Sydney | `Australia/Sydney` |
| Auckland | `Pacific/Auckland` |
| Fiji | `Pacific/Fiji` |

### Africa

| Location | Value |
|---|---|
| Cairo | `Africa/Cairo` |
| Johannesburg/Cape Town | `Africa/Johannesburg` |
| Lagos | `Africa/Lagos` |
| Nairobi | `Africa/Nairobi` |

## Examples

### UTC everywhere

```json
"TimeZone": "UTC"
```

### UK local time with daylight saving

```json
"TimeZone": "Europe/London"
```

### US Eastern time

```json
"TimeZone": "America/New_York"
```

### Australian Eastern time

```json
"TimeZone": "Australia/Sydney"
```

## List every timezone available on the host

Windows PowerShell:

```powershell
[TimeZoneInfo]::GetSystemTimeZones() | Select-Object Id, DisplayName
```

Linux:

```bash
timedatectl list-timezones
```

Choose an IANA-style ID where available, restart IW4MAdmin, and check the active timezone shown on the evidence dashboard.

Next: [[Configuration]] or [[Troubleshooting]].
