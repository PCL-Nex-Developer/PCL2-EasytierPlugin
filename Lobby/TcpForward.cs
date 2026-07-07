using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;

namespace PCL.EasyTierPlugin.Lobby;

public sealed class TcpForward(
    IPAddress listenAddress,
    int listenPort,
    IPAddress targetAddress,
    int targetPort,
    int maxConnections = 10)
    : IDisposable
{
    private Socket? _listenerSocket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _connectionSemaphore = new(maxConnections, maxConnections);
    private readonly ConcurrentDictionary<Guid, ConnectionPair> _activeConnections = new();

    private bool _isRunning;
    private bool _disposed;

    public int LocalPort { get; private set; }

    public int ActiveConnections => _activeConnections.Count;

    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;

        try
        {
            _listenerSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            };

            _listenerSocket.Bind(new IPEndPoint(listenAddress, listenPort));
            _listenerSocket.Listen(100);

            if (_listenerSocket.LocalEndPoint is not IPEndPoint endPoint) throw new InvalidCastException("出现了意外的转换操作");
            LocalPort = endPoint.Port;

            _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token), _cts.Token);

            LogWrapper.Info("TcpForward", $"MC 端口转发已启动，监听 {listenAddress}:{LocalPort}，目标 {targetAddress}:{targetPort}");
        }
        catch (Exception ex)
        {
            _isRunning = false;
            LogWrapper.Error(ex, "TcpForward", $"启动 MC 端口转发时发生错误: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _cts?.Cancel();
        _isRunning = false;

        foreach (var connection in _activeConnections.Values)
        {
            connection.ClientSocket.SafeClose();
            connection.TargetSocket.SafeClose();
        }
        _activeConnections.Clear();

        _listenerSocket?.SafeClose();

        LogWrapper.Info("TcpForward", "MC 端口转发已停止");
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(() => _listenerSocket.SafeClose());
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_listenerSocket is null) break;
                var clientSocket = await _listenerSocket.AcceptAsync(cancellationToken);

                if (_activeConnections.Count >= maxConnections)
                {
                    clientSocket.SafeClose();
                    LogWrapper.Warn("TcpForward", $"已达到最大连接数限制({maxConnections})，拒绝新连接");
                    continue;
                }

                await _connectionSemaphore.WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleConnectionAsync(clientSocket, cancellationToken), cancellationToken)
                    .ContinueWith(_ => _connectionSemaphore.Release(), TaskScheduler.Default);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "TcpForward", "接受连接时发生错误");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleConnectionAsync(Socket clientSocket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid();

        try
        {
            LogWrapper.Info("TcpForward", $"接受来自 {clientSocket.RemoteEndPoint} 的连接");

            var targetSocket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            };

            await targetSocket.ConnectAsync(targetAddress, targetPort, cancellationToken);

            var connectionPair = new ConnectionPair(clientSocket, targetSocket);
            _activeConnections[connectionId] = connectionPair;

            LogWrapper.Info("TcpForward", $"开始端口转发 {clientSocket.RemoteEndPoint} <-> {targetSocket.RemoteEndPoint}({connectionId})");

            var forwardTask1 = ForwardDataAsync(clientSocket, targetSocket, cancellationToken);
            var forwardTask2 = ForwardDataAsync(targetSocket, clientSocket, cancellationToken);

            await Task.WhenAny(forwardTask1, forwardTask2);

            Console.WriteLine($"端口转发 {connectionId} 已完成");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理连接 {connectionId} 时发生错误: {ex.Message}");
        }
        finally
        {
            clientSocket.SafeClose();
            _activeConnections.TryRemove(connectionId, out _);
        }
    }

    private static async Task ForwardDataAsync(Socket source, Socket destination, CancellationToken cancellationToken)
    {
        using var bufferOwner = MemoryPool<byte>.Shared.Rent(8192);
        try
        {
            var buffer = bufferOwner.Memory;
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await source.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (bytesRead == 0) break;

                await destination.SendAsync(buffer[..bytesRead], SocketFlags.None, cancellationToken);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;
        if (_disposed) return;
        Stop();
        _cts?.Dispose();
        _connectionSemaphore.Dispose();
        _disposed = true;
    }

    ~TcpForward()
    {
        Dispose(false);
    }

    private sealed class ConnectionPair(Socket clientSocket, Socket targetSocket)
    {
        public Socket ClientSocket { get; } = clientSocket;
        public Socket TargetSocket { get; } = targetSocket;
    }
}
