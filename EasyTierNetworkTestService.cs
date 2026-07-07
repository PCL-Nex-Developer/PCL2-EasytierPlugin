using System.Threading;
using System.Threading.Tasks;
using PCL.EasyTierPlugin.EasyTier;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

internal sealed class EasyTierNetworkTestService : ILobbyNetworkTestService
{
    public async Task<LobbyNetworkTestResult?> TestAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await CliNetTest.GetNetStatusAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (status is null) return null;

        return new LobbyNetworkTestResult
        {
            UdpNatType = Map(status.UdpNatType),
            TcpNatType = Map(status.TcpNatType),
            SupportIPv6 = status.SupportIPv6
        };
    }

    private static LobbyNatType Map(CliNetTest.NatType type) => type switch
    {
        CliNetTest.NatType.OpenInternet => LobbyNatType.OpenInternet,
        CliNetTest.NatType.NoPat => LobbyNatType.NoPat,
        CliNetTest.NatType.FullCone => LobbyNatType.FullCone,
        CliNetTest.NatType.Restricted => LobbyNatType.Restricted,
        CliNetTest.NatType.PortRestricted => LobbyNatType.PortRestricted,
        CliNetTest.NatType.SymmetricEasy => LobbyNatType.SymmetricEasy,
        CliNetTest.NatType.Symmetric => LobbyNatType.Symmetric,
        CliNetTest.NatType.SymmetricFirewall => LobbyNatType.SymmetricFirewall,
        CliNetTest.NatType.UdpBlocked => LobbyNatType.UdpBlocked,
        _ => LobbyNatType.Unknown
    };
}
