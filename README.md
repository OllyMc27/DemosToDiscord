# DemosToDiscord

Automatically uploads Plutonium demo files to Discord when a REPORT is triggered in IW4MAdmin.

## Features

✅ Supports T5 and T6  
✅ Automatically selects the correct demo based on map, gamemode and start time  
✅ Includes T6 JSON sidecar file in uploads  
✅ Prevents wrong demo selection when servers run the same maps  
✅ Waits for match end and file unlock before uploading  
✅ Simple install (drop DLL and restart)

---

## Installation

1. Download the DLL from Releases
2. Copy it to your IW4MAdmin Plugins folder
3. Restart IW4MAdmin
4. Edit `DemosToDiscord.json`
5. Add your webhook + demo paths
6. Restart IW4MAdmin again

---

## Configuration

Example config:

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
