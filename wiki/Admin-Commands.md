# Admin Commands

| Command | Permission | Purpose |
|---|---|---|
| `!dtdstatus` | Moderator | Shows enabled state, queue and evidence totals. |
| `!dtdstats` | Moderator | Shows aggregate retained evidence totals. |
| `!dtdfind <case-id>` | Moderator | Previews the best current demo candidate for a case. |
| `!dtdtest` | SeniorAdmin | Tests the default Discord webhook. |
| `!dtdretry <case-id>` | SeniorAdmin | Requeues a failed or missing-demo case. |
| `!dtdbaseline` | Moderator | Shows proactive source events, cached members, refresh time and telemetry health. |
| `!dtdrebuildbaseline` | SeniorAdmin | Forces a full proactive baseline rebuild. |

## Examples

```text
!dtdstatus
```

```text
!dtdfind a57f42d0c9c8
```

```text
!dtdretry a57f42d0c9c8
```

```text
!dtdbaseline
```

`!dtdrebuildbaseline` can scan a large `EFClientKills` table and should be used during a quiet period. Normal operation refreshes incrementally.

The case ID appears on the evidence queue, case page and Discord embed.

`!dtdfind` does not upload or change the case; it reports the candidate that would currently be selected. `!dtdretry` changes the case back to queued and lets a worker search again.

Command prefixes follow the IW4MAdmin server configuration. The examples use `!`.

Next: [[Troubleshooting]] or [[Webfront Guide|Webfront-Guide]].
