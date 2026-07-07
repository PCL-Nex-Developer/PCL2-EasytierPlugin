using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin.EasyTier;

internal static class EasyTierDependencyManager
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public static async Task<string> EnsureReadyAsync(IPluginContext context, IPluginLogger log, CancellationToken cancellationToken)
    {
        var easyTierFilePath = EasyTierMetadata.GetEasyTierFilePath(context.DataDirectory);
        EasyTierMetadata.UsePluginDataDirectory(context.DataDirectory);

        if (HasCoreFiles(easyTierFilePath)) return easyTierFilePath;

        await InstallLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasCoreFiles(easyTierFilePath)) return easyTierFilePath;

            context.Host.Core.Hint("正在下载 EasyTier 联机组件...", PluginHintType.Info);
            Directory.CreateDirectory(easyTierFilePath);

            var tempDir = Path.Combine(context.DataDirectory, "cache");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, $"easytier-{EasyTierMetadata.CurrentEasyTierVer}-{EasyTierMetadata.ArchitectureName}.zip");

            await DownloadArchiveAsync(zipPath, log, cancellationToken).ConfigureAwait(false);
            ZipFile.ExtractToDirectory(zipPath, Path.Combine(easyTierFilePath, ".."), true);

            try
            {
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                log.Warn("Failed to delete EasyTier download cache.", ex);
            }

            if (!HasCoreFiles(easyTierFilePath))
            {
                throw new FileNotFoundException("EasyTier files are missing after extraction.");
            }

            context.Host.Core.Hint("EasyTier 联机组件已就绪", PluginHintType.Success);
            return easyTierFilePath;
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static bool HasCoreFiles(string easyTierFilePath) =>
        File.Exists(Path.Combine(easyTierFilePath, "easytier-core.exe")) &&
        File.Exists(Path.Combine(easyTierFilePath, "easytier-cli.exe")) &&
        File.Exists(Path.Combine(easyTierFilePath, "Packet.dll"));

    private static async Task DownloadArchiveAsync(string zipPath, IPluginLogger log, CancellationToken cancellationToken)
    {
        var urls = new[]
        {
            $"https://staticassets.naids.com/resources/pclce/static/easytier/easytier-windows-{EasyTierMetadata.ArchitectureName}-v{EasyTierMetadata.CurrentEasyTierVer}.zip",
            $"https://s3.pysio.online/pcl2-ce/static/easytier/easytier-windows-{EasyTierMetadata.ArchitectureName}-v{EasyTierMetadata.CurrentEasyTierVer}.zip",
            $"https://github.com/EasyTier/EasyTier/releases/download/v{EasyTierMetadata.CurrentEasyTierVer}/easytier-windows-{EasyTierMetadata.ArchitectureName}-v{EasyTierMetadata.CurrentEasyTierVer}.zip"
        };

        Exception? lastError = null;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL.EasyTierPlugin/1.0");

        foreach (var url in urls)
        {
            try
            {
                log.Info($"Downloading EasyTier from {url}");
                await using var source = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

                var fileInfo = new FileInfo(zipPath);
                if (fileInfo.Length < 64 * 1024)
                {
                    throw new InvalidDataException("Downloaded EasyTier archive is too small.");
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                log.Warn($"Failed to download EasyTier from {url}", ex);
            }
        }

        throw new InvalidOperationException("Failed to download EasyTier dependency files.", lastError);
    }
}