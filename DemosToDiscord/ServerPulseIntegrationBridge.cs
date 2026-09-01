using System.Text.Json;

namespace DemosToDiscord;

public sealed record ServerPulseCaseRequest(
    string SourceEventId,
    DateTime CapturedAtUtc,
    string ServerId,
    string ServerName,
    long? LegacyServerId,
    string Game,
    string Map,
    string Mode,
    int TargetClientId,
    long TargetNetworkId,
    string TargetName,
    string Category,
    string Accusation,
    IReadOnlyList<string> Context,
    int AdminClientId,
    string AdminName,
    string Notes);

public static class ServerPulseIntegrationBridge
{
    private static readonly object Gate = new();
    private static Func<ServerPulseCaseRequest, CancellationToken, Task<string>>? _handler;

    internal static void Register(Func<ServerPulseCaseRequest, CancellationToken, Task<string>> handler)
    {
        lock (Gate)
            _handler = handler;
    }

    internal static void Unregister()
    {
        lock (Gate)
            _handler = null;
    }

    public static Task<string> SubmitAsync(string json, CancellationToken token)
    {
        var request = JsonSerializer.Deserialize<ServerPulseCaseRequest>(json,
                          new JsonSerializerOptions(JsonSerializerDefaults.Web))
                      ?? throw new ArgumentException("ServerPulse supplied an empty case request.");
        Func<ServerPulseCaseRequest, CancellationToken, Task<string>>? handler;
        lock (Gate)
            handler = _handler;
        return handler is null
            ? Task.FromException<string>(new InvalidOperationException("DemosToDiscord has not finished loading."))
            : handler(request, token);
    }
}
