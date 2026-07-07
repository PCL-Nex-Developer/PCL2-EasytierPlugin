using PCL.Core.App;
using PCL.EasyTierPlugin.Lobby;
using PCL.EasyTierPlugin.Scaffolding.Client;
using PCL.EasyTierPlugin.Scaffolding.Client.Models;
using PCL.EasyTierPlugin.Scaffolding.Exceptions;
using PCL.EasyTierPlugin.Scaffolding.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PCL.Core.IO.Net;
using PCL.Core.Utils.Secret;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin.Scaffolding;

public static class ScaffoldingFactory
{
    private static ILobbyTunnelProvider? _provider;

    public static void SetProvider(ILobbyTunnelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public static void ClearProvider(ILobbyTunnelProvider provider)
    {
        if (ReferenceEquals(_provider, provider)) _provider = null;
    }

    // Please update ScaffoldingServerContext.cs at the same time.
    private static string _LobbyVendor(ILobbyTunnelProvider provider) =>
        $"PCL Nex {Basics.VersionName}, {provider.DisplayName} {provider.Version}";

    /// <exception cref="ArgumentException">Invalid lobby code.</exception>
    /// <exception cref="FailedToGetPlayerException">Thrown if failed to get host player info.</exception>
    /// <exception cref="InvalidOperationException">Failed to initialize lobby tunnel.</exception>
    public static async Task<ScaffoldingClientEntity> CreateClientAsync
        (string playerName, string lobbyCode, LobbyType from)
    {
        var machineId = Identify.LauncherId;

        if (!LobbyCodeGenerator.TryParse(lobbyCode, out var info))
        {
            throw new ArgumentException("Invalid lobby code.", nameof(lobbyCode));
        }

        var provider = _GetTunnelProvider();
        var tunnel = await provider.CreateSessionAsync(new LobbyTunnelSessionOptions
        {
            LobbyCode = info.FullCode,
            NetworkName = info.NetworkName,
            NetworkSecret = info.NetworkSecret,
            MachineId = machineId,
            PlayerName = playerName,
            IsHost = false,
            MinecraftPort = 0,
            ScaffoldingPort = 0,
            RelayServers = _GetRelayServers(),
            PreferredProtocol = EasyTierPluginSettings.ProtocolPreference.ToString().ToLowerInvariant(),
            TryPunchSymmetricNat = EasyTierPluginSettings.TryPunchSymmetricNat,
            EnableIPv6 = EasyTierPluginSettings.EnableIPv6,
            UseLatencyFirstMode = EasyTierPluginSettings.UseLatencyFirstMode,
            EnableDebugOutput = EasyTierPluginSettings.EnableDebugOutput
        }).ConfigureAwait(false);

        var hostInfo = tunnel.HostPeer;

        if (hostInfo is null)
        {
            await tunnel.DisposeAsync().ConfigureAwait(false);
            throw new FailedToGetPlayerException("Can not get the host information.");
        }

        if (!int.TryParse(hostInfo.HostName[22..], out var scfPort))
        {
            await tunnel.DisposeAsync().ConfigureAwait(false);
            throw new ArgumentException("Invalid hostname.", nameof(hostInfo));
        }

        var localPort = await tunnel.AddPortForwardAsync(hostInfo.Ip, scfPort).ConfigureAwait(false);

        var client = new ScaffoldingClient("127.0.0.1", localPort, playerName, machineId, _LobbyVendor(provider));

        return new ScaffoldingClientEntity(client, tunnel, hostInfo);
    }

    /// <summary>
    /// Create Scaffolding Server.
    /// </summary>
    /// <param name="mcPort">Target forward Miencraft shared port.</param>
    /// <param name="playerName">Game player name.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Failed to launch lobby tunnel.</exception>
    public static ScaffoldingServerEntity CreateServer(int mcPort, string playerName)
    {
        var provider = _GetTunnelProvider();
        var context = ScaffoldingServerContext.Create(playerName, mcPort, _LobbyVendor(provider));
        var scfPort = NetworkHelper.NewTcpPort();

        var tunnel = provider.CreateSessionAsync(new LobbyTunnelSessionOptions
        {
            LobbyCode = context.UserLobbyInfo.FullCode,
            NetworkName = context.UserLobbyInfo.NetworkName,
            NetworkSecret = context.UserLobbyInfo.NetworkSecret,
            MachineId = Identify.LauncherId,
            PlayerName = playerName,
            IsHost = true,
            MinecraftPort = mcPort,
            ScaffoldingPort = scfPort,
            RelayServers = _GetRelayServers(),
            PreferredProtocol = EasyTierPluginSettings.ProtocolPreference.ToString().ToLowerInvariant(),
            TryPunchSymmetricNat = EasyTierPluginSettings.TryPunchSymmetricNat,
            EnableIPv6 = EasyTierPluginSettings.EnableIPv6,
            UseLatencyFirstMode = EasyTierPluginSettings.UseLatencyFirstMode,
            EnableDebugOutput = EasyTierPluginSettings.EnableDebugOutput
        }).GetAwaiter().GetResult();

        var server = new ScaffoldingServer(scfPort, context);

        return new ScaffoldingServerEntity(server, tunnel);
    }

    private static ILobbyTunnelProvider _GetTunnelProvider()
    {
        var provider = _provider;

        if (provider is null)
        {
            throw new InvalidOperationException("No lobby tunnel provider is registered. Please install or enable a lobby tunnel plugin.");
        }

        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(provider.UnavailableReason ??
                                                $"Lobby tunnel provider {provider.DisplayName} is unavailable.");
        }

        return provider;
    }

    private static IReadOnlyList<string> _GetRelayServers()
    {
        var customRelays = EasyTierPluginSettings.CustomRelayServer
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return LobbyRelay.RelayList
            .Where(relay =>
                (relay.Type == LobbyRelayType.Selfhosted && EasyTierPluginSettings.ServerType != 2) ||
                (relay.Type == LobbyRelayType.Community && EasyTierPluginSettings.ServerType == 1))
            .Select(relay => relay.Url)
            .Concat(customRelays)
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public record ScaffoldingClientEntity(ScaffoldingClient Client, ILobbyTunnelSession Tunnel, LobbyTunnelPeerInfo HostInfo);

public record ScaffoldingServerEntity(ScaffoldingServer Server, ILobbyTunnelSession Tunnel);
