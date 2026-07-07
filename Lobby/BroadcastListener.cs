using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.EasyTierPlugin.Lobby;

public sealed class BroadcastListener(bool receiveLocalOnly = true) : IDisposable
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.2.60");
    private static readonly IPAddress MulticastAddressV6 = IPAddress.Parse("ff75:230::60");
    private static readonly Regex BroadcastMotd = new(@"\[MOTD\](.*?)\[/MOTD\]", RegexOptions.Compiled);
    private static readonly Regex BroadcastAd = new(@"\[AD\](.*?)\[/AD\]", RegexOptions.Compiled);

    private UdpClient? _client;
    private UdpClient? _clientV6;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _listenTaskV6;
    private bool _isDisposed;

    public event Action<BroadcastRecord, IPEndPoint>? OnReceive;

    public void Start()
    {
        if (_client is not null || _clientV6 is not null) return;
        _cts = new CancellationTokenSource();

        _client = new UdpClient();
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
        _client.JoinMulticastGroup(MulticastAddress);

        _clientV6 = new UdpClient(AddressFamily.InterNetworkV6);
        _clientV6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _clientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, 4445));
        _clientV6.JoinMulticastGroup(MulticastAddressV6);

        _listenTask = ListenThreadAsync(_client);
        _listenTaskV6 = ListenThreadAsync(_clientV6);
    }

    private async Task ListenThreadAsync(UdpClient? client)
    {
        while (_cts is not null && client is not null && !_cts.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(_cts.Token);
                var message = Encoding.UTF8.GetString(result.Buffer);

                if (!TryParseServerInfo(message, out var serverInfo) || serverInfo is null) continue;
                if (receiveLocalOnly && !IsAddressLocal(result.RemoteEndPoint.Address)) continue;
                OnReceive?.Invoke(serverInfo, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing packet: {ex.Message}");
            }
        }
    }

    private static bool IsAddressLocal(IPAddress address) => NetworkInterface.GetAllNetworkInterfaces()
        .Where(static item => item.OperationalStatus == OperationalStatus.Up)
        .SelectMany(static item => item.GetIPProperties().UnicastAddresses)
        .Select(static item => item.Address)
        .Where(static item => item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
        .Where(static item => !item.Equals(IPAddress.Any) && !item.Equals(IPAddress.IPv6Any))
        .Where(static item => !item.Equals(IPAddress.Loopback) && !item.Equals(IPAddress.IPv6Loopback))
        .Contains(address);

    private static bool TryParseServerInfo(string rawMessage, out BroadcastRecord? serverInfo)
    {
        var motdMatch = BroadcastMotd.Match(rawMessage);
        var adMatch = BroadcastAd.Match(rawMessage);

        if (!adMatch.Success || !int.TryParse(adMatch.Groups[1].Value.Trim(), out var port))
        {
            serverInfo = null;
            return false;
        }

        serverInfo = new BroadcastRecord(
            motdMatch.Success ? motdMatch.Groups[1].Value.Trim() : "missing no",
            new IPEndPoint(IPAddress.Loopback, port),
            DateTime.Now);
        return true;
    }

    public void Stop()
    {
        if (_client is null) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _client?.Close();
        _client?.Dispose();
        _client = null;
        _clientV6?.Close();
        _clientV6?.Dispose();
        _clientV6 = null;

        _listenTask?.Wait(500);
        _listenTaskV6?.Wait(500);
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _isDisposed = true;
        Stop();
    }
}
