[![Version](https://img.shields.io/github/v/release/OllyMc27/DemosToDiscord?label=version&style=flat-square)](https://github.com/OllyMc27/DemosToDiscord/releases)
[![Downloads](https://img.shields.io/github/downloads/OllyMc27/DemosToDiscord/total?label=downloads&style=flat-square)](https://github.com/OllyMc27/DemosToDiscord/releases)
[![Issues](https://img.shields.io/github/issues/OllyMc27/DemosToDiscord?style=flat-square)](https://github.com/OllyMc27/DemosToDiscord/issues)


# DemosToDiscord

Automatically uploads Plutonium demo files to Discord when a player is reported in IW4MAdmin.

This plugin is designed to work **alongside existing IW4MAdmin → Discord bridge plugins** such as:

- [**BetterIW4ToDiscord**](https://github.com/Ayymoss/BetterIW4ToDiscord)
- [**YADB – Yet Another Discord Bridge**](https://forum.awog.at/topic/89/release-yadb-yet-another-discord-bridge)

- Those plugins handle general chat, reports, and events —  
**DemosToDiscord’s job is to automatically attach the actual demo files** to Discord when a report occurs.

This removes manual effort from staff by:
- Finding the correct demo file automatically
- Waiting for the match to end
- Uploading the demo and metadata
- Posting a clean, structured embed into Discord for review

---

## ✅ Features

- Supports **T5 (Black Ops 1)** and **T6 (Black Ops 2)**
- Automatically selects the correct demo using:
  - Map name
  - Game mode
  - Timestamp window
- Uploads `.demo` and optional T6 `.json` files
- Captures the map name at report-time (prevents wrong-map embeds)
- Prevents incorrect demo selection when multiple servers run the same maps
- Waits for match end and file unlock before upload
- Clean Discord embeds with player and server context
- Simple setup — drop the DLL and restart

---

## 🧠 How It Works

1. A player is reported in-game
2. The plugin captures:
   - Current server
   - Map name
   - Game mode
   - Report time
3. The demo folder is scanned for matching files
4. The correct demo is selected based on:
   - Timestamp window
   - Map name
   - Game mode
5. The plugin waits for:
   - Match to finish
   - Demo file to be unlocked
6. The demo is uploaded to Discord with a formatted embed

---

## 📸 Example Discord Messages

Below are real examples of what the plugin posts into Discord.

---

### ✅ Demo Successfully Uploaded

> When a demo file is found, it is automatically uploaded with full context for staff to review.

![Demo Uploaded Example](docs/example-demo-upload.png)

Includes:
- Server name
- Game (T5 / T6)
- Map name
- Reported player
- Reporter
- GUID
- Clickable web profile
- Attached demo file
- Plugin version and timestamp

---

### ⚠ No Demo Found

> If no demo is found within the configured time window, the report is still posted so staff are aware.

![No Demo Example](docs/example-no-demo.png)

Includes:
- Full report context
- Explicit "No demo found" status
- Same layout and formatting

---

## 🛠 Installation

1. Download the DLL from the **Releases** page
2. Copy it to:
   ```
   IW4MAdmin/Plugins/
   ```
3. Restart IW4MAdmin
4. Edit `DemosToDiscord.json`
5. Add your webhook and demo paths
6. Restart IW4MAdmin again

---

## ⚙ Configuration

Example `DemosToDiscord.json`:

```json
{
  "Webhook": "https://discord.com/api/webhooks/...",
  "T5DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t5\\demos",
  "T6DemoPath": "C:\\Users\\Administrator\\AppData\\Local\\Plutonium\\storage\\t6\\demos",
  "MaxLookbackMinutes": 90,
  "MaxWaitMinutes": 30,
  "RetryIntervalSeconds": 20,
  "PostMatchDelaySeconds": 10
}
```

---

## 📄 Notes

- T6 JSON sidecar files are uploaded automatically if available
- Map names always reflect the report-time map (even after rotation)
- Demo filename format must match Plutonium defaults
- Ensure IW4MAdmin has file access to demo directories

---

## ✅ Requirements

- IW4MAdmin (current version)
- .NET 8 runtime
- Plutonium demo recording enabled

---

## 👤 Author

Developed by **OllyMc27**

---

Contributions, feedback, and suggestions are welcome 👍
