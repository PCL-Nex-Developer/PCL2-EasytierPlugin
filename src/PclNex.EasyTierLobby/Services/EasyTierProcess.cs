using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PclNex.EasyTierLobby.Lobby;

namespace PclNex.EasyTierLobby.Services;

internal sealed class EasyTierProcess : IAsyncDisposable
{
    private const string CurrentEasyTierVersion = "2.6.4";
    private const string HostIp = "10.114.51.41";

    private readonly Process _process;
    private readonly IPluginLog _log;

    private EasyTierProcess(Process process, int rpcPort, IPluginLog log)
    {
        _process = process;
        RpcPort = rpcPort;
        _log = log;
    }

    public int RpcPort { get; }

    public static string ResolveEasyTierDirectory(string dataDirectory)
    {
        var explicitDir = Environment.GetEnvironmentVariable("PCLNEX_EASYTIER_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return explicitDir;
        }

        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        return Path.Combine(dataDirectory, "EasyTier", CurrentEasyTierVersion, $"easytier-windows-{arch}");
    }

    public static bool IsExplicitEasyTierDirectoryConfigured()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PCLNEX_EASYTIER_DIR"));

    public static bool IsComplete(string easyTierDirectory)
        => File.Exists(Path.Combine(easyTierDirectory, "easytier-core.exe")) &&
           File.Exists(Path.Combine(easyTierDirectory, "easytier-cli.exe")) &&
           File.Exists(Path.Combine(easyTierDirectory, "Packet.dll"));

    public static EasyTierProcess Create(
        string easyTierDirectory,
        LobbyInfo lobby,
        int minecraftPort,
        int scaffoldingPort,
        bool asHost,
        string machineId,
        IReadOnlyList<EasyTierRelay> relays,
        IPluginLog log)
    {
        if (!IsComplete(easyTierDirectory))
        {
            throw new FileNotFoundException("EasyTier core files are missing.", easyTierDirectory);
        }

        foreach (var existingProcess in Process.GetProcessesByName("easytier-core"))
        {
            log.Warn($"Detected existing EasyTier process: {existingProcess.Id}. It may affect this lobby.");
        }

        var rpcPort = NetworkPorts.GetFreeTcpPort();
        var args = new ArgumentsBuilder()
            .AddFlag("no-tun")
            .AddFlag("multi-thread")
            .AddFlag("enable-kcp-proxy")
            .AddFlag("enable-quic-proxy")
            .Add("encryption-algorithm", "aes-gcm")
            .Add("compression", "zstd")
            .Add("default-protocol", "tcp")
            .Add("network-name", lobby.NetworkName)
            .Add("network-secret", lobby.NetworkSecret)
            .Add("machine-id", machineId)
            .Add("rpc-portal", $"127.0.0.1:{rpcPort}")
            .Add("private-mode", "true")
            .AddFlag("p2p-only")
            .Add("l", "tcp://0.0.0.0:0")
            .Add("l", "udp://0.0.0.0:0");

        if (asHost)
        {
            args.AddWithSpace("i", HostIp)
                .Add("hostname", $"scaffolding-mc-server-{scaffoldingPort}")
                .Add("tcp-whitelist", scaffoldingPort.ToString())
                .Add("udp-whitelist", scaffoldingPort.ToString())
                .Add("tcp-whitelist", minecraftPort.ToString())
                .Add("udp-whitelist", minecraftPort.ToString());
        }
        else
        {
            args.AddFlag("d")
                .Add("hostname", Guid.NewGuid().ToString("N"))
                .Add("tcp-whitelist", "0")
                .Add("udp-whitelist", "0");
        }

        foreach (var node in GetPeerNodes(relays))
        {
            args.Add("p", node);
        }

        var process = new Process
        {
            EnableRaisingEvents = true,
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(easyTierDirectory, "easytier-core.exe"),
                WorkingDirectory = easyTierDirectory,
                Arguments = args.ToString(),
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        return new EasyTierProcess(process, rpcPort, log);
    }

    public void Start()
    {
        _log.Info("Starting EasyTier core.");
        _process.Start();
    }

    public async Task<int> AddPortForwardAsync(string remoteIp, int remotePort, CancellationToken cancellationToken = default)
    {
        var localPort = NetworkPorts.GetFreeTcpPort();
        await RunCliAsync(["port-forward", "add", "tcp", $"127.0.0.1:{localPort}", $"{remoteIp}:{remotePort}"], cancellationToken).ConfigureAwait(false);
        await RunCliAsync(["port-forward", "add", "udp", $"127.0.0.1:{localPort}", $"{remoteIp}:{remotePort}"], cancellationToken).ConfigureAwait(false);
        await RunCliAsync(["port-forward", "add", "tcp", $"[::1]:{localPort}", $"{remoteIp}:{remotePort}"], cancellationToken).ConfigureAwait(false);
        await RunCliAsync(["port-forward", "add", "udp", $"[::1]:{localPort}", $"{remoteIp}:{remotePort}"], cancellationToken).ConfigureAwait(false);
        return localPort;
    }

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                if (await GetHostAsync(timeoutCts.Token).ConfigureAwait(false) is not null)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(1000, timeoutCts.Token).ConfigureAwait(false);
        }

        throw new TimeoutException("EasyTier did not become ready within 30 seconds.");
    }

    public async Task<EasyTierPeer?> GetHostAsync(CancellationToken cancellationToken = default)
    {
        var peers = await GetPeersAsync(cancellationToken).ConfigureAwait(false);
        return peers.FirstOrDefault(peer => peer.Hostname.StartsWith("scaffolding-mc-server-", StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<EasyTierPeer>> GetPeersAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunCliAsync(["peer"], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<EasyTierPeer>>(output) ?? [];
        }
        catch (JsonException ex)
        {
            _log.Error(ex, "Failed to parse EasyTier peer output.");
            return [];
        }
    }

    private async Task<string> RunCliAsync(params string[] arguments)
        => await RunCliAsync(arguments, CancellationToken.None).ConfigureAwait(false);

    private async Task<string> RunCliAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        var cliPath = Path.Combine(_process.StartInfo.WorkingDirectory!, "easytier-cli.exe");
        var cliArguments = new ArgumentsBuilder()
            .AddWithSpace("rpc-portal", $"127.0.0.1:{RpcPort}")
            .AddWithSpace("o", "json");

        foreach (var argument in arguments)
        {
            cliArguments.AddRaw(argument);
        }

        using var cliProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                WorkingDirectory = _process.StartInfo.WorkingDirectory,
                Arguments = cliArguments.ToString(),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        cliProcess.Start();
        var stdout = await cliProcess.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await cliProcess.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await cliProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (cliProcess.ExitCode != 0)
        {
            throw new InvalidOperationException($"easytier-cli failed: {stderr}");
        }

        return stdout;
    }

    private static IEnumerable<string> GetPeerNodes(IReadOnlyList<EasyTierRelay> relays)
    {
        var customNodes = Environment.GetEnvironmentVariable("PCLNEX_EASYTIER_PEERS");
        if (!string.IsNullOrWhiteSpace(customNodes))
        {
            foreach (var node in customNodes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return node;
            }
        }

        foreach (var node in relays.Select(relay => relay.Url).Where(url => !string.IsNullOrWhiteSpace(url)))
        {
            yield return node;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }
}

internal sealed class ArgumentsBuilder
{
    private readonly List<string> _args = [];

    public ArgumentsBuilder AddFlag(string name)
    {
        _args.Add($"--{name}");
        return this;
    }

    public ArgumentsBuilder Add(string name, string value)
    {
        _args.Add($"{Prefix(name)}={Escape(value)}");
        return this;
    }

    public ArgumentsBuilder AddWithSpace(string name, string value)
    {
        _args.Add(Prefix(name));
        _args.Add(Escape(value));
        return this;
    }

    public ArgumentsBuilder AddRaw(string value)
    {
        _args.Add(Escape(value));
        return this;
    }

    public override string ToString() => string.Join(' ', _args);

    private static string Prefix(string name) => name.Length == 1 ? $"-{name}" : $"--{name}";

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}

internal sealed record EasyTierPeer
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }

    [JsonPropertyName("ipv4")]
    public required string Ipv4 { get; init; }

    [JsonPropertyName("cost")]
    public string Cost { get; init; } = string.Empty;

    [JsonPropertyName("lat_ms")]
    public string Ping { get; init; } = string.Empty;

    [JsonPropertyName("loss_rate")]
    public string Loss { get; init; } = string.Empty;

    [JsonPropertyName("nat_type")]
    public string NatType { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string EasyTierVersion { get; init; } = string.Empty;
}
