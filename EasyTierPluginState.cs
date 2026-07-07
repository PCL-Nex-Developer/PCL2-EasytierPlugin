using PCL.Core.App.Configuration;

namespace PCL.EasyTierPlugin;

internal static class EasyTierPluginState
{
    private const string KeyPrefix = "Plugin.pclnex.easytier.";
    private const string LegacyKeyPrefix = "Plugin.pcl.easytier.lobby.";

    private static IConfigProvider Shared => ConfigService.GetProvider(ConfigSource.Shared);
    private static IConfigProvider SharedEncrypt => ConfigService.GetProvider(ConfigSource.SharedEncrypt);

    public static bool IsEulaAccepted
    {
        get => GetShared("eula.accepted", false);
        set => Shared.SetValue(KeyPrefix + "eula.accepted", value);
    }

    public static string AnnouncementCache
    {
        get => GetShared("announcement.cache", string.Empty);
        set => Shared.SetValue(KeyPrefix + "announcement.cache", value ?? string.Empty);
    }

    public static int AnnouncementCacheVersion
    {
        get => GetShared("announcement.cacheVersion", 0);
        set => Shared.SetValue(KeyPrefix + "announcement.cacheVersion", value);
    }

    public static string NaidRefreshToken
    {
        get => GetEncrypted("naid.refreshToken", string.Empty);
        set => SharedEncrypt.SetValue(KeyPrefix + "naid.refreshToken", value ?? string.Empty);
    }

    public static string NaidRefreshExpireTime
    {
        get => GetEncrypted("naid.refreshExpireTime", string.Empty);
        set => SharedEncrypt.SetValue(KeyPrefix + "naid.refreshExpireTime", value ?? string.Empty);
    }

    public static void ResetAnnouncementCache()
    {
        Shared.Delete(KeyPrefix + "announcement.cache");
        Shared.Delete(KeyPrefix + "announcement.cacheVersion");
    }

    public static void ResetAccount()
    {
        SharedEncrypt.Delete(KeyPrefix + "naid.refreshToken");
        SharedEncrypt.Delete(KeyPrefix + "naid.refreshExpireTime");
    }

    private static T GetShared<T>(string key, T defaultValue)
    {
        if (Shared.GetValue(KeyPrefix + key, out T? value)) return value;
        return Shared.GetValue(LegacyKeyPrefix + key, out value) ? value : defaultValue;
    }

    private static T GetEncrypted<T>(string key, T defaultValue)
    {
        if (SharedEncrypt.GetValue(KeyPrefix + key, out T? value)) return value;
        return SharedEncrypt.GetValue(LegacyKeyPrefix + key, out value) ? value : defaultValue;
    }
}
