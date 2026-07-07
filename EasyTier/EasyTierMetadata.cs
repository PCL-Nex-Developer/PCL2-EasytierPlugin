using System.IO;
using System.Runtime.InteropServices;
using PCL.Core.App;

namespace PCL.EasyTierPlugin.EasyTier;

public static class EasyTierMetadata
{
    public const string CurrentEasyTierVer = "2.6.4";

    public static string ArchitectureName => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";

    public static string EasyTierFilePath { get; private set; } = GetLegacyEasyTierFilePath();

    public static void UsePluginDataDirectory(string pluginDataDirectory)
        => EasyTierFilePath = GetEasyTierFilePath(pluginDataDirectory);

    public static string GetEasyTierFilePath(string pluginDataDirectory) => Path.Combine(
        pluginDataDirectory,
        "EasyTier",
        CurrentEasyTierVer,
        $"easytier-windows-{ArchitectureName}");

    private static string GetLegacyEasyTierFilePath() => Path.Combine(
        Paths.SharedLocalData,
        "EasyTier",
        CurrentEasyTierVer,
        $"easytier-windows-{ArchitectureName}");
}