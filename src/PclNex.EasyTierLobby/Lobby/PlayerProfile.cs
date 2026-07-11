using System.Text.Json.Serialization;

namespace PclNex.EasyTierLobby.Lobby;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum PlayerKind
{
    HOST,
    GUEST
}

internal sealed record PlayerProfile
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("machine_id")]
    public required string MachineId { get; init; }

    [JsonPropertyName("vendor")]
    public required string Vendor { get; init; }

    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerKind? Kind { get; init; }
}
