using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PclNex.EasyTierLobby.Services;

internal sealed class MinecraftLanBroadcaster(string description, int localPort) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Socket? _socket;
    private Task? _broadcastTask;

    public void Start()
    {
        if (_broadcastTask is not null)
        {
            return;
        }

        _broadcastTask = Task.Run(() => BroadcastLoopAsync(_cts.Token));
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        _socket = new Socket(SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        var buffer = Encoding.UTF8.GetBytes($"[MOTD]{description}[/MOTD][AD]{localPort}[/AD]");
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4445);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _socket.SendToAsync(buffer, SocketFlags.None, endpoint, cancellationToken).ConfigureAwait(false);
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket?.Dispose();
        _cts.Dispose();
    }
}
