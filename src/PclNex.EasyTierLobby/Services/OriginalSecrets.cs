namespace PclNex.EasyTierLobby.Services;

internal static class OriginalSecrets
{
    private const string DefaultLobbyDefaultSecret = "PCLCEETLOBBY2025";
    private const string DefaultNatayarkClientId = "pclce2025";
    private const string DefaultNatayarkClientSecret = "$2y$10$bOfGWPKw2zwB.EYvHxmiN.F1AN3fvUbruNrhL0QMyCYcTsOiMO2Ta";
    private const string DefaultLinkServerRoot = "https://staticassets.naids.com/resources/pclce|https://s3.pysio.online/pcl2-ce";

    public static string LobbyDefaultSecret => GetSecret("LOBBY_DEFAULT_SECRET") ?? DefaultLobbyDefaultSecret;

    public static string NatayarkClientId => GetSecret("NAID_CLIENT_ID") ?? DefaultNatayarkClientId;

    public static string NatayarkClientSecret => GetSecret("NAID_CLIENT_SECRET") ?? DefaultNatayarkClientSecret;

    public static string[] LinkServers => (GetSecret("LINK_SERVER_ROOT") ?? DefaultLinkServerRoot)
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? TelemetryKey => GetSecret("TELEMETRY_KEY") ?? GetSecret("TelemetryKey");

    public static void ApplyToProcessEnvironment()
    {
        SetEnvironmentDefault("PCL_LOBBY_DEFAULT_SECRET", LobbyDefaultSecret);
        SetEnvironmentDefault("PCL_NAID_CLIENT_ID", NatayarkClientId);
        SetEnvironmentDefault("PCL_NAID_CLIENT_SECRET", NatayarkClientSecret);
        SetEnvironmentDefault("PCL_LINK_SERVER_ROOT", string.Join('|', LinkServers));

        var telemetryKey = TelemetryKey;
        if (!string.IsNullOrWhiteSpace(telemetryKey))
        {
            SetEnvironmentDefault("PCL_TELEMETRY_KEY", telemetryKey);
            SetEnvironmentDefault("PCL_TelemetryKey", telemetryKey);
        }
    }

    private static string? GetSecret(string key)
        => Environment.GetEnvironmentVariable($"PCL_{key}")
           ?? Environment.GetEnvironmentVariable(key);

    private static void SetEnvironmentDefault(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

}
