# Native web bundle development

The `feature/native-web-bundle` branch moves Cheating Case Review from a legacy template interaction to the native `/demos-to-discord` Razor route.

## What changed

- IW4MAdmin's page list now supplies the **Cheating Case Review** administrator sidebar entry.
- The route reads the logged-in user's client ID and permission claims before rendering.
- Queue, filter, case and Discord links remain under the native route.
- Bundle-owned responsive layout CSS is shipped in `wwwroot` and scoped by the host.
- Existing review, delete and Send to Discord actions still use the same permission-checked backend services.
- Demo discovery, evidence storage, report grouping, proactive evaluation, ServerPulse integration, Discord messages and flagged-player notifications are unchanged.

The Razor page deliberately calls the established case renderer during this first migration stage. This keeps the evidence workflow stable while later work can split queues, case panels and actions into smaller interactive Razor components.

## Build and install

```powershell
dotnet build DemosToDiscord.sln -c Release
```

The bundle is written to `DemosToDiscord/bin/Release/DemosToDiscord.zip`.

This ZIP requires an IW4MAdmin host with the experimental plugin-bundle support described in the [official bundle guide](https://github.com/RaidMax/IW4M-Admin/blob/feature/plugin-data-directories/docs/plugin-web-bundles.md). Put the ZIP in `IW4MAdmin/Plugins` and restart. Do not install both the bundle and the standalone DLL at the same time.

The production `master` branch and current releases remain standalone-DLL builds until the bundle host is ready for normal deployment.
