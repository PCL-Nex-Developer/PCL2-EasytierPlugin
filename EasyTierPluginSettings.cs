using System;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

internal static class EasyTierPluginSettings
{
    private static IPluginConfigApi? _config;

    public static void Initialize(IPluginConfigApi config) => _config = config;

    private static IPluginConfigApi Config => _config ?? throw new InvalidOperationException("EasyTier plugin settings are not initialized.");

    public static string Username
    {
        get => Config.GetString("lobby.username", string.Empty);
        set => Config.Set("lobby.username", value ?? string.Empty);
    }

    public static int ServerType
    {
        get => Config.GetInt("lobby.serverType", 1);
        set => Config.Set("lobby.serverType", value);
    }

    public static bool UseLatencyFirstMode
    {
        get => Config.GetBool("lobby.latencyFirstMode", true);
        set => Config.Set("lobby.latencyFirstMode", value);
    }

    public static string CustomRelayServer
    {
        get => Config.GetString("lobby.customRelayServer", string.Empty);
        set => Config.Set("lobby.customRelayServer", value ?? string.Empty);
    }

    public static LobbyProtocolPreference ProtocolPreference
    {
        get
        {
            var raw = Config.GetString("lobby.protocolPreference", LobbyProtocolPreference.Tcp.ToString());
            return Enum.TryParse<LobbyProtocolPreference>(raw, true, out var value) ? value : LobbyProtocolPreference.Tcp;
        }
        set => Config.Set("lobby.protocolPreference", value.ToString());
    }

    public static bool TryPunchSymmetricNat
    {
        get => Config.GetBool("lobby.tryPunchSymmetricNat", true);
        set => Config.Set("lobby.tryPunchSymmetricNat", value);
    }

    public static bool EnableIPv6
    {
        get => Config.GetBool("lobby.enableIPv6", true);
        set => Config.Set("lobby.enableIPv6", value);
    }

    public static bool EnableDebugOutput
    {
        get => Config.GetBool("lobby.enableDebugOutput", false);
        set => Config.Set("lobby.enableDebugOutput", value);
    }

    public static LobbySettingsSnapshot Snapshot => new()
    {
        Username = Username,
        ServerType = ServerType,
        UseLatencyFirstMode = UseLatencyFirstMode,
        CustomRelayServer = CustomRelayServer,
        ProtocolPreference = ProtocolPreference,
        TryPunchSymmetricNat = TryPunchSymmetricNat,
        EnableIPv6 = EnableIPv6,
        EnableDebugOutput = EnableDebugOutput
    };

    public static void Update(LobbySettingsSnapshot settings)
    {
        Username = settings.Username;
        ServerType = settings.ServerType;
        UseLatencyFirstMode = settings.UseLatencyFirstMode;
        CustomRelayServer = settings.CustomRelayServer;
        ProtocolPreference = settings.ProtocolPreference;
        TryPunchSymmetricNat = settings.TryPunchSymmetricNat;
        EnableIPv6 = settings.EnableIPv6;
        EnableDebugOutput = settings.EnableDebugOutput;
    }

    public static void Reset()
    {
        foreach (var key in new[]
        {
            "lobby.username",
            "lobby.serverType",
            "lobby.latencyFirstMode",
            "lobby.customRelayServer",
            "lobby.protocolPreference",
            "lobby.tryPunchSymmetricNat",
            "lobby.enableIPv6",
            "lobby.enableDebugOutput"
        })
        {
            Config.Remove(key);
        }
    }
}
