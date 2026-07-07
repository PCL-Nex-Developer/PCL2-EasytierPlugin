using System.Collections.Generic;

namespace PCL.EasyTierPlugin.Lobby;

public sealed class LobbyRelay
{
    public static List<LobbyRelay> RelayList { get; set; } = [];

    public required string Url { get; init; }
    public required string Name { get; init; }
    public LobbyRelayType Type { get; init; }
}

public enum LobbyRelayType
{
    Community,
    Selfhosted,
    Custom
}