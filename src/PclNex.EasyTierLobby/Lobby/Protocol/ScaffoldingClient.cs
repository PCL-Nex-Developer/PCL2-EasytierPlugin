using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using PclNex.EasyTierLobby.Services;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal sealed class ScaffoldingClient(
    string host,
    int scfPort,
    string playerName,
    string machineId,
    string vendor,
    IPluginLog log) : IAsyncDisposable
{
    private readonly SemaphoreSlim _sendReceiveLock = new(1, 1);
    private readonly PlayerPingRequest _playerPingRequest = new(playerName, machineId, vendor);
    private TcpClient? _tcpClient;
    private PipeReader? _pipeReader;
    private PipeWriter? _pipeWriter;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private bool _connected;

    public event Action<IReadOnlyList<PlayerProfile>, long>? Heartbeat;
    public event Action? ServerStopped;

    public IReadOnlyList<PlayerProfile>? PlayerList { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connected)
        {
            return;
        }

        _tcpClient = new TcpClient();
        try
        {
            log.Info($"Connecting to scaffolding server {host}:{scfPort}.");
            await _tcpClient.ConnectAsync(host, scfPort, cancellationToken).ConfigureAwait(false);

            var stream = _tcpClient.GetStream();
            _pipeReader = PipeReader.Create(stream);
            _pipeWriter = PipeWriter.Create(stream);

            await SendRequestAsync(_playerPingRequest, cancellationToken).ConfigureAwait(false);
            _connected = true;
            StartHeartbeats();
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            ServerStopped?.Invoke();
            throw;
        }
    }

    public async Task<TResponse> SendRequestAsync<TResponse>(
        IScaffoldingRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        if (_pipeWriter is null || _pipeReader is null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        await _sendReceiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ProtocolWriter.WriteRequestAsync(_pipeWriter, request, cancellationToken).ConfigureAwait(false);
            var response = await ProtocolReader.ReadResponseAsync(_pipeReader, cancellationToken).ConfigureAwait(false);
            return request.ParseResponseBody(response.Body);
        }
        finally
        {
            _sendReceiveLock.Release();
        }
    }

    private void StartHeartbeats()
    {
        _heartbeatCts = new CancellationTokenSource();
        _heartbeatTask = HeartbeatLoopAsync(_heartbeatCts.Token);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var timer = new Stopwatch();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                timer.Restart();
                await SendRequestAsync(_playerPingRequest, cancellationToken).ConfigureAwait(false);
                timer.Stop();

                PlayerList = await SendRequestAsync(new GetPlayerProfileListRequest(), cancellationToken).ConfigureAwait(false);
                Heartbeat?.Invoke(PlayerList, timer.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Error(ex, "Scaffolding heartbeat failed; server may have stopped.");
                ServerStopped?.Invoke();
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connected = false;
        if (_heartbeatCts is not null)
        {
            await _heartbeatCts.CancelAsync().ConfigureAwait(false);
        }

        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _heartbeatCts?.Dispose();
        _tcpClient?.Dispose();
        _sendReceiveLock.Dispose();
    }
}
