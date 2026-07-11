using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace PclNex.EasyTierLobby.Services;

internal sealed class EasyTierDownloader(string dataDirectory, IPluginLog log)
{
    private const string CurrentEasyTierVersion = "2.6.4";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    public async Task EnsureAsync(string easyTierDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (EasyTierProcess.IsComplete(easyTierDirectory))
        {
            return;
        }

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(easyTierDirectory)!);

        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        var fileName = $"easytier-windows-{architecture}-v{CurrentEasyTierVersion}.zip";
        var tempRoot = Path.Combine(dataDirectory, "Temp");
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, fileName);

        Exception? lastException = null;
        foreach (var root in OriginalSecrets.LinkServers)
        {
            var url = $"{root.TrimEnd('/')}/static/easytier/{fileName}";
            try
            {
                progress?.Report($"正在下载 EasyTier：{url}");
                await using var source = await _httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(zipPath);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                lastException = null;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                log.Warn($"Failed to download EasyTier from {url}: {ex.Message}");
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException("EasyTier 下载失败。", lastException);
        }

        progress?.Report("正在解压 EasyTier...");
        var versionRoot = Path.GetDirectoryName(easyTierDirectory)!;
        if (Directory.Exists(versionRoot))
        {
            Directory.Delete(versionRoot, recursive: true);
        }

        Directory.CreateDirectory(versionRoot);
        ZipFile.ExtractToDirectory(zipPath, versionRoot, overwriteFiles: true);
        File.Delete(zipPath);

        if (!EasyTierProcess.IsComplete(easyTierDirectory))
        {
            throw new InvalidOperationException($"EasyTier 解压完成但文件不完整：{easyTierDirectory}");
        }

        progress?.Report("EasyTier 已准备完成。");
    }
}
