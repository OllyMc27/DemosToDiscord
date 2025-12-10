namespace DemosToDiscord;

public class DemosToDiscordConfig
{
    public string Webhook { get; set; } = string.Empty;

    public string T5DemoPath { get; set; } =
        @"C:\Users\Administrator\AppData\Local\Plutonium\storage\t5\demos";

    public string T6DemoPath { get; set; } =
        @"C:\Users\Administrator\AppData\Local\Plutonium\storage\t6\demos";

    // Currently unused but kept for backwards compatibility / future use
    public int MaxLookbackMinutes { get; set; } = 90;

    // How long we keep searching for a demo file after a report
    public int MaxWaitMinutes { get; set; } = 30;

    // How often to poll the demo folder while searching for a new demo
    public int RetryIntervalSeconds { get; set; } = 20;

    // How long to wait AFTER the map/mode changes before starting file checks / upload
    public int PostMatchDelaySeconds { get; set; } = 10;

    // For future toggle of verbose logging
    public bool Debug { get; set; } = false;

    // For future use if you want to rename files on upload
    public bool RenameOnUpload { get; set; } = true;
}
