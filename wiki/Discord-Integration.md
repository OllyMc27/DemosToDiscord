# Discord Integration

## Creating a webhook

In Discord, open the destination channel's settings, create a webhook and copy its complete URL into `Webhook`.

```json
"Webhook": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN"
```

Treat this URL as a secret. Anyone with it can post through the webhook.

## Routing order

The plugin selects a webhook in this order:

1. matching `ServerOverrides` webhook;
2. matching `GameWebhooks` entry;
3. default `Webhook`.

This permits one global channel, separate channels per game, or a dedicated channel for an individual server.

## Per-game example

```json
"GameWebhooks": {
  "T5": "https://discord.com/api/webhooks/T5_ID/T5_TOKEN",
  "T6": "https://discord.com/api/webhooks/T6_ID/T6_TOKEN"
}
```

## Role mentions

```json
"ReportRoleId": "123456789012345678",
"AntiCheatRoleId": "234567890123456789",
"MentionRolesOnlyWhenDemoReady": true
```

- Use the numeric role ID, not its name.
- The webhook/channel must be allowed to mention the role.
- Empty values disable mentions.
- Server overrides can replace both role IDs.

## Evidence messages

A message can include:

- player name, client ID and network ID;
- case ID and capture time;
- game, map and mode;
- server name and endpoint;
- evidence source and confidence;
- proactive risk score and strongest contributing indicators, when applicable;
- review and assignment state;
- demo status, filename and size;
- match timeline and report offsets;
- reports or anti-cheat detection;
- direct case and player-profile links.

![Discord evidence message with original demo, JSON metadata and case links](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/discord-evidence-ready.png)

## Attachments

T6 evidence normally includes:

- the original `.demo`;
- the original `.json` metadata sidecar.

T5 evidence includes the original `.demo`. Files are uploaded directly—no ZIP archive and no filename changes.

## Metadata-only messages

`SendMetadataOnlyCasesToDiscord` controls notifications for T4, IW5, T5 Zombies and servers explicitly marked as not supporting demos.

```json
"SendMetadataOnlyCasesToDiscord": true
```

When disabled, the case remains in the webfront without creating a Discord message.

## Proactive notifications

Proactive cases are posted only when all three conditions are true:

- the assessment reached `ProactiveCaseRiskThreshold` and was retained;
- it also reached `ProactiveDiscordRiskThreshold`;
- `EnableProactiveDiscordNotifications` is `true`.

The message identifies the case as proactive, shows the explainable risk score/signals and links to normal human review. It never announces that the player is definitively cheating and never applies a penalty. See [[Proactive Detection|Proactive-Detection]].

## Case and profile links

Set IW4MAdmin's `Webfront.ManualUrl` to the public webfront address. Without it, Discord cannot construct a usable external review URL.

The embed title and **Review evidence case** link open the case. **Open player profile** opens the native IW4MAdmin profile.

## Message updates

When a case is assigned or reviewed, DemosToDiscord edits the original message rather than sending a duplicate. Existing demo/JSON attachments remain attached.

![Completed review reflected in the original Discord evidence message](https://raw.githubusercontent.com/OllyMc27/DemosToDiscord/master/docs/images/discord-review-completed.png)

## CDN download links

Discord attachment URLs can change or expire. The webfront queries the original webhook message to obtain fresh attachment URLs instead of permanently storing one CDN address.

## Webhook test

Run:

```text
!dtdtest
```

This tests the default webhook and requires SeniorAdmin. It does not test every per-game or per-server route.

Next: [[Configuration]], [[Troubleshooting]] or [[Privacy and Data|Privacy-and-Data]].
