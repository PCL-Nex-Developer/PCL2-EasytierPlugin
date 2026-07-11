using System.Net;
using System.Net.Sockets;

namespace PclNex.EasyTierLobby.Services;

internal static class NetworkPorts
{
    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
