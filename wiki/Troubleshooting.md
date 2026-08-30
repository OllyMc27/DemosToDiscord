# Troubleshooting

## Plugin missing from the loaded list

- Confirm the file is named `DemosToDiscord.dll`.
- Put it directly in `IW4MAdmin/Plugins`.
- Remove duplicate or version-suffixed copies.
- Confirm the DLL targets the same current IW4MAdmin/.NET generation.
- Restart the complete IW4MAdmin process.
- Check the startup log for dependency or configuration exceptions.

## Configuration file is not generated

- Confirm the plugin loaded successfully first.
- Check write permission on `IW4MAdmin/Configuration`.
- Look for invalid JSON in an existing configuration file.
- Compare it with the complete [[Configuration]] example.

## Case stays on Searching

- Wait for the match to end; Plutonium may keep the demo open during play.
- Confirm the correct T5/T6 path for the IW4MAdmin account.
- Confirm a recent filename has the expected map and time.
- Check `MaxLookbackMinutes` and `MaxWaitMinutes`.
- Run `!dtdfind <case-id>`.
- Temporarily enable `Debug` and inspect candidate results.

## Case says Demo missing

For supported T5/T6 multiplayer:

- verify the demo directory exists;
- verify the IW4MAdmin account can read it;
- compare the event time with the filename;
- verify the map;
- ensure the match actually completed;
- inspect debug rejection reasons.

T4, IW5 and T5 Zombies are intentionally metadata-only and should say Demo unsupported instead.

## Wrong mode shown or mode mismatch

T6 demo filenames use game codes such as:

- `dm` = Free-for-All;
- `war` = Team Deathmatch;
- `koth` = Hardpoint;
- `hq` = Headquarters.

IW4MAdmin can briefly report the previous/live mode around a rotation. T6 therefore treats mode as a scoring preference when map, time and target GUID identify a stronger candidate. See [[Demo Matching|Demo-Matching]].

## No Discord message

- Run `!dtdtest` for the default webhook.
- Check whether `GameWebhooks` or `ServerOverrides` changes the destination.
- Confirm the webhook still exists.
- Confirm the channel permits webhook messages and file uploads.
- Check the case error box and full IW4MAdmin log response.
- For unsupported demos, confirm `SendMetadataOnlyCasesToDiscord` is `true`.

## Discord returns HTTP 400

Read the response body in the case or log. Common causes include:

- invalid webhook formatting;
- an invalid/reserved webhook display name;
- a file exceeding Discord's upload limit;
- malformed role IDs or mention restrictions.

Discord rejects webhook usernames containing its reserved service name. DemosToDiscord does not override the current webhook display name; rename it in Discord if necessary.

## Role is not mentioned

- Use the numeric role ID, not the role name.
- Confirm the role ID is 17–20 digits.
- Allow the webhook/channel to mention the role.
- Check `MentionRolesOnlyWhenDemoReady`.
- Check for server-specific role overrides.

## Review link missing or incorrect

Set IW4MAdmin's `Webfront.ManualUrl` to the externally reachable base URL, including `https://` and a port if required. Restart IW4MAdmin.

## Review link opens only after refresh

Use the current plugin version, which opts case links out of same-route enhanced navigation. Clear browser cache after replacing the DLL and restart IW4MAdmin.

## Download link expired

Open the case page again. It refreshes attachment information from the original Discord message. If the original message or webhook was deleted, the attachment cannot be recovered through the plugin.

## Downloaded demo does not appear in theatre

- Keep the original `.demo` filename.
- Keep the matching T6 `.json` filename.
- Place both in the correct game's Plutonium demo folder.
- Do not rename them after downloading.
- Confirm the file belongs to the same game/storage profile.

## Moderator cannot access Demo Evidence

- Check `WebfrontMinimumPermission`.
- Check the moderator's current IW4MAdmin level.
- Confirm the dashboard is enabled.
- Confirm interaction read/write permission has not been overridden.

## Reports were not cleared

Case clearing only targets active reports attached to that case. Legacy cases without penalty IDs use a narrow player/time match. Unrelated reports are intentionally preserved.

## Manual ban created unexpected evidence

Use a current 2.3.x build. Manual bans should only link to a recent existing case and should not create a separate case on the player's current/unrelated server.

## Times are incorrect

- Check `TimeZone` spelling.
- Use an IANA identifier from [[Language and Timezones|Language-and-Timezones]].
- Restart IW4MAdmin after changing it.
- Invalid values fall back to `Europe/London` and log a warning.

## Collecting useful debug information

1. Set `"Debug": true`.
2. Restart IW4MAdmin.
3. Reproduce one case.
4. Save startup, penalty-event and demo-scan lines.
5. Remove webhook URLs and sensitive identifiers.
6. Set `Debug` back to `false` and restart.

Include the plugin version, game, case ID, server timezone, report time, demo filename and relevant error when opening an issue.
