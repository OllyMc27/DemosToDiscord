# Privacy and Data

## Retained case metadata

`Configuration/DemosToDiscordCases.json` can contain:

- player name, client ID and network ID;
- server name, endpoint, game, map and mode;
- report times, reporter details and optionally reasons;
- anti-cheat detection labels and penalty references;
- administrator-resolved ServerPulse accusation text, bounded nearby chat context and the resolving administrator's note;
- original demo filename, file size and timing metadata;
- Discord message, channel and guild identifiers;
- assignment, reviewer, decision, notes and audit history.

`Configuration/DemosToDiscordProactiveBaselines.json` contains a rebuildable compact cache of aggregate, server-aware statistical populations used by proactive review. Proactive cases also retain their point-in-time score and contributing signal explanations.

## Data not copied into the state file

The plugin does not store:

- raw demo contents;
- raw T6 JSON contents;
- permanent copies of Discord CDN URLs;
- webhook secrets inside the case file;
- raw player chat unrelated to the administrator-reviewed ServerPulse context window.

Demo and JSON contents remain in their original Plutonium folders and Discord attachments.

## Retention and deletion

Ordinary cases are pruned by `CaseRetentionDays` and `MaxStoredCases`. Cases reviewed as **cheating — action taken** are deliberately protected from both automatic limits. An IW4MAdmin Owner can permanently delete retained case metadata through the confirmed case-page action; doing so does not reverse a penalty or delete Discord evidence.

The proactive baseline cache is derived from the live IW4MAdmin database and can be deleted while IW4MAdmin is stopped if a full rebuild is required.

## Reduce retained data

```json
"StoreReportReasons": false,
"CaseRetentionDays": 30,
"MaxStoredCases": 200
```

When reason storage is disabled, the case records that storage was disabled rather than retaining the original text.

## Protect the configuration

`DemosToDiscord.json` contains webhook secrets. Restrict filesystem access and never upload the live file to GitHub, Discord support channels or public issue trackers.

If a webhook URL is exposed, delete or regenerate the webhook in Discord and update the configuration.

## Protect the Discord channel

Evidence messages can reveal player identifiers, reporter information and server endpoints. Use a restricted staff channel with appropriate retention and access policies.

ServerPulse community-signal context is copied only after an administrator chooses to create a DemosToDiscord case. Keep Cheating Case Review and its Discord destination staff-only.

## Screenshots and support logs

Before publishing screenshots:

- blur public IP addresses and ports if necessary;
- hide webhook URLs completely;
- anonymise player GUIDs/network IDs if they are not required;
- consider anonymising reporter names;
- include only the log window needed to demonstrate the problem.

Next: [[Configuration]], [[Discord Integration|Discord-Integration]] or [[Screenshots]].
