using System.Net.Sockets;

namespace PCL.EasyTierPlugin.Lobby;

internal static class SocketExtensions
{
    public static void SafeClose(this Socket? socket)
    {
        if (socket is null) return;

        try
        {
            if (socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            socket.Close();
        }
        catch
        {
        }
    }
}
