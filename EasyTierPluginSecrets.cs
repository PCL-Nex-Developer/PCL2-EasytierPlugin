using System.Collections.Generic;
using PCL.Core.Utils.OS;

namespace PCL.EasyTierPlugin;

internal static class EasyTierPluginSecrets
{
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["LINK_SERVER_ROOT"] = "https://staticassets.naids.com/resources/pclce|https://s3.pysio.online/pcl2-ce",
        ["NAID_CLIENT_ID"] = "pclce2025",
        ["NAID_CLIENT_SECRET"] = "$2y$10$bOfGWPKw2zwB.EYvHxmiN.F1AN3fvUbruNrhL0QMyCYcTsOiMO2Ta"
    };

    public static string? Get(string key, bool readEnv = true, bool readEnvDebugOnly = false)
    {
        return EnvironmentInterop.GetSecret(key, readEnv, readEnvDebugOnly) ??
               (Defaults.TryGetValue(key, out var value) ? value : null);
    }
}