using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.EasyTierPlugin.EasyTier;
using PCL.EasyTierPlugin.Scaffolding.Client.Models;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

internal sealed class EasyTierLobbyTunnelProvider(IPluginContext context) : ILobbyTunnelProvider
{
    private readonly IPluginLogger _log = context.Host.Core.GetLogger("EasyTier");

    public string Id => "pcl.easytier";
    public string DisplayName => "EasyTier";
    public string Version => EasyTierMetadata.CurrentEasyTierVer;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public async Task<ILobbyTunnelSession> CreateSessionAsync(LobbyTunnelSessionOptions options, CancellationToken cancellationToken = default)
    {
        await EasyTierDependencyManager.EnsureReadyAsync(context, _log, cancellationToken).ConfigureAwait(false);

        var lobby = new LobbyInfo(options.LobbyCode, options.NetworkName, options.NetworkSecret);
        var entity = new EasyTierEntity(lobby, options.MinecraftPort, options.ScaffoldingPort, options.IsHost);
        if (entity.Launch() != 0)
        {
            throw new InvalidOperationException("Failed to launch EasyTier.");
        }

        var session = new EasyTierLobbyTunnelSession(options, entity);
        await session.InitializePeerInfoAsync().ConfigureAwait(false);
        return session;
    }
}

internal sealed class EasyTierLobbyTunnelSession(LobbyTunnelSessionOptions options, EasyTierEntity entity) : ILobbyTunnelSession
{
    public string LobbyCode => options.LobbyCode;
    public int MinecraftPort => entity.MinecraftPort;
    public LobbyTunnelPeerInfo? HostPeer { get; private set; }

    public async Task<int> AddPortForwardAsync(string targetIp, int targetPort, CancellationToken cancellationToken = default)
        => await entity.AddPortForwardAsync(targetIp, targetPort).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await entity.StopAsync().ConfigureAwait(false);
    }

    public async Task InitializePeerInfoAsync()
    {
        var (_, players) = await entity.CheckEasyTierStatusAsync().ConfigureAwait(false);
        HostPeer = players?.Host is null ? null : Convert(players.Host);
    }

    private static LobbyTunnelPeerInfo Convert(EasyPlayerInfo info) => new()
    {
        IsHost = info.IsHost,
        HostName = info.HostName,
        Ip = info.Ip,
        UserName = info.UserName,
        MinecraftName = info.MinecraftName,
        Way = info.Way switch
        {
            ConnectionWay.Local => LobbyTunnelConnectionWay.Local,
            ConnectionWay.P2P => LobbyTunnelConnectionWay.P2P,
            ConnectionWay.Relay => LobbyTunnelConnectionWay.Relay,
            _ => LobbyTunnelConnectionWay.Unknown
        },
        Ping = info.Ping,
        Loss = info.Loss,
        NatType = info.NatType,
        ProviderVersion = info.EasyTierVer
    };
}
