using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PclNex.EasyTierLobby.Services;

internal sealed class LobbyAnnouncementService(IPluginLog log)
{
    private const int ProtocolVersion = 6;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(7) };

    public LobbyAnnouncement Current { get; private set; } = LobbyAnnouncement.OfflineFallback;

    public async Task<LobbyAnnouncement> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;
        foreach (var root in OriginalSecrets.LinkServers)
        {
            try
            {
                var url = $"{root.TrimEnd('/')}/api/link/v2/announce.json";
                var json = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                var dto = JsonSerializer.Deserialize<LobbyAnnouncementDto>(json, JsonOptions())
                          ?? throw new InvalidOperationException("Lobby announcement is empty.");

                Current = new LobbyAnnouncement(
                    dto.Available && dto.Version <= ProtocolVersion,
                    dto.AllowCustomName,
                    dto.RequireLogin,
                    dto.RequireRealname,
                    dto.Version,
                    dto.Notices.Select(notice => new LobbyNotice(notice.Type ?? "notice", notice.Content ?? string.Empty)).ToArray(),
                    dto.Relays
                        .Where(relay => !string.IsNullOrWhiteSpace(relay.Url))
                        .Select(relay => new EasyTierRelay(relay.Name ?? "Relay", relay.Url!, relay.Type ?? "community"))
                        .ToArray());

                log.Info($"Loaded lobby announcement from {url}.");
                return Current;
            }
            catch (Exception ex)
            {
                lastException = ex;
                log.Warn($"Failed to load lobby announcement from {root}: {ex.Message}");
            }
        }

        if (lastException is not null)
        {
            log.Error(lastException, "Failed to load all lobby announcement endpoints; using fallback relay list.");
        }

        Current = LobbyAnnouncement.OfflineFallback;
        return Current;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed record LobbyAnnouncement(
    bool Available,
    bool AllowCustomName,
    bool RequireLogin,
    bool RequireRealname,
    double Version,
    IReadOnlyList<LobbyNotice> Notices,
    IReadOnlyList<EasyTierRelay> Relays)
{
    public static LobbyAnnouncement OfflineFallback { get; } = new(
        Available: true,
        AllowCustomName: true,
        RequireLogin: true,
        RequireRealname: true,
        Version: 6,
        Notices: [],
        Relays:
        [
            new EasyTierRelay("Default", "tcp://public.easytier.top:11010", "community"),
            new EasyTierRelay("Default", "tcp://public2.easytier.cn:54321", "community"),
            new EasyTierRelay("Default", "https://etnode.zkitefly.eu.org/node1", "community"),
            new EasyTierRelay("Default", "https://etnode.zkitefly.eu.org/node2", "community"),
            new EasyTierRelay("Default", "https://etnode.zkitefly.eu.org/-node1", "community"),
            new EasyTierRelay("Default", "https://etnode.zkitefly.eu.org/-node2", "community")
        ]);
}

internal sealed record LobbyNotice(string Type, string Content);

internal sealed record EasyTierRelay(string Name, string Url, string Type);

internal sealed record LobbyAnnouncementDto
{
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    [JsonPropertyName("allowCustomName")]
    public bool AllowCustomName { get; init; }

    [JsonPropertyName("requireLogin")]
    public bool RequireLogin { get; init; } = true;

    [JsonPropertyName("requireRealname")]
    public bool RequireRealname { get; init; } = true;

    [JsonPropertyName("version")]
    public double Version { get; init; }

    [JsonPropertyName("notices")]
    public LobbyNoticeDto[] Notices { get; init; } = [];

    [JsonPropertyName("relays")]
    public LobbyRelayDto[] Relays { get; init; } = [];
}

internal sealed record LobbyNoticeDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed record LobbyRelayDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
