using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PclNex.EasyTierLobby.Services;

namespace PclNex.EasyTierLobby.Lobby.Protocol;

internal sealed class ScaffoldingServer : IAsyncDisposable
{
    private static readonly TimeSpan PlayerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly ScaffoldingServerContext _context;
    private readonly IPluginLog _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private Task? _cleanupTask;

    public ScaffoldingServer(int port, ScaffoldingServerContext context, IPluginLog log)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _context = context;
        _log = log;
    }

    public event Action<IReadOnlyList<PlayerProfile>>? ServerStarted;
    public event Action? ServerStopped;
    public event Action<Exception?>? ServerException;
    public event Action<IReadOnlyList<PlayerProfile>>? PlayerProfilePing;

    public void Start()
    {
        try
        {
            _listener.Start();
            _log.Info($"Scaffolding server listening on {_listener.LocalEndpoint}.");
        }
        catch (SocketException ex)
        {
            ServerException?.Invoke(ex);
            return;
        }

        _context.PlayerProfilesPing += players => PlayerProfilePing?.Invoke(players);
        _listenTask = ListenForClientsAsync(_cts.Token);
        _cleanupTask = MonitorPlayerLivenessAsync(_cts.Token);
        ServerStarted?.Invoke(_context.PlayerProfiles);
    }

    private async Task MonitorPlayerLivenessAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, cancellationToken).ConfigureAwait(false);
                var now = DateTime.UtcNow;
                var timedOutPlayerKeys = new List<string>();

                foreach (var (machineId, trackedPlayer) in _context.TrackedPlayers)
                {
                    if (trackedPlayer.Profile.Kind is PlayerKind.HOST)
                    {
                        continue;
                    }

                    if (now - trackedPlayer.LastSeenUtc > PlayerTimeout)
                    {
                        timedOutPlayerKeys.Add(machineId);
                    }
                }

                var changed = false;
                foreach (var key in timedOutPlayerKeys)
                {
                    changed |= _context.TrackedPlayers.TryRemove(key, out _);
                }

                if (changed)
                {
                    _context.OnPlayerProfilesChanged();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Player cleanup failed.");
            }
        }
    }

    private async Task ListenForClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleClientAsync(tcpClient, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Scaffolding listener failed.");
                ServerStopped?.Invoke();
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using (tcpClient)
        {
            var stream = tcpClient.GetStream();
            var buffer = new byte[128 * 1024];
            var pending = new List<byte>();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        break;
                    }

                    pending.AddRange(buffer.AsSpan(0, count).ToArray());
                    while (TryParseFrame(pending, out var requestFrame))
                    {
                        var (status, body) = await HandleFrameAsync(requestFrame.TypeInfo, requestFrame.Body, sessionId, cancellationToken)
                            .ConfigureAwait(false);

                        var responseHeader = new byte[5];
                        responseHeader[0] = status;
                        BinaryPrimitives.WriteUInt32BigEndian(responseHeader.AsSpan(1), (uint)body.Length);
                        await stream.WriteAsync(responseHeader, cancellationToken).ConfigureAwait(false);
                        if (!body.IsEmpty)
                        {
                            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Scaffolding client session failed.");
            }
        }
    }

    private Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleFrameAsync(
        string typeInfo,
        ReadOnlyMemory<byte> body,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return typeInfo switch
            {
                "c:player_ping" => HandlePlayerPingAsync(body, cancellationToken),
                "c:server_port" => HandleGetServerPortAsync(),
                "c:player_profiles_list" => HandleGetPlayerProfileListAsync(),
                "c:protocols" => HandleGetProtocolsAsync(),
                "c:ping" => Task.FromResult((Status: (byte)0, Body: body)),
                _ => Task.FromResult((Status: (byte)64, Body: ReadOnlyMemory<byte>.Empty))
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Failed to handle scaffolding request {typeInfo} from {sessionId}.");
            return Task.FromResult((Status: (byte)255, Body: (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(ex.Message)));
        }
    }

    private Task<(byte Status, ReadOnlyMemory<byte> Body)> HandlePlayerPingAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<PlayerProfile>(body.Span, LobbyJson.Options);
            if (profile is null || string.IsNullOrWhiteSpace(profile.MachineId))
            {
                return Task.FromResult((Status: (byte)32, Body: ReadOnlyMemory<byte>.Empty));
            }

            var guestProfile = profile with { Kind = PlayerKind.GUEST };
            var changed = false;
            _context.TrackedPlayers.AddOrUpdate(
                guestProfile.MachineId,
                _ =>
                {
                    changed = true;
                    return new TrackedPlayerProfile(guestProfile, DateTime.UtcNow);
                },
                (_, existing) => existing with { Profile = guestProfile, LastSeenUtc = DateTime.UtcNow });

            if (changed)
            {
                _context.OnPlayerProfilesChanged();
            }

            return Task.FromResult((Status: (byte)0, Body: ReadOnlyMemory<byte>.Empty));
        }
        catch (JsonException)
        {
            return Task.FromResult((Status: (byte)32, Body: ReadOnlyMemory<byte>.Empty));
        }
    }

    private Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleGetServerPortAsync()
    {
        if (_context.MinecraftServerPort <= 0)
        {
            return Task.FromResult((Status: (byte)32, Body: ReadOnlyMemory<byte>.Empty));
        }

        var portBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)_context.MinecraftServerPort);
        return Task.FromResult((Status: (byte)0, Body: (ReadOnlyMemory<byte>)portBytes));
    }

    private Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleGetPlayerProfileListAsync()
    {
        var responseBody = JsonSerializer.SerializeToUtf8Bytes(_context.PlayerProfiles, LobbyJson.Options);
        return Task.FromResult((Status: (byte)0, Body: (ReadOnlyMemory<byte>)responseBody));
    }

    private static Task<(byte Status, ReadOnlyMemory<byte> Body)> HandleGetProtocolsAsync()
    {
        string[] protocols = ["c:ping", "c:protocols", "c:server_port", "c:player_ping", "c:player_profiles_list"];
        var responseBody = Encoding.ASCII.GetBytes(string.Join('\0', protocols));
        return Task.FromResult((Status: (byte)0, Body: (ReadOnlyMemory<byte>)responseBody));
    }

    private static bool TryParseFrame(List<byte> pending, out (string TypeInfo, ReadOnlyMemory<byte> Body) frame)
    {
        frame = default;
        if (pending.Count < 1)
        {
            return false;
        }

        var typeLength = pending[0];
        if (typeLength is 0 or > 128)
        {
            throw new InvalidDataException($"Invalid frame type length: {typeLength}.");
        }

        if (pending.Count < 1 + typeLength + 4)
        {
            return false;
        }

        var bytes = pending.ToArray();
        var headerStart = 1 + typeLength;
        var bodyLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(headerStart, 4));
        if (bodyLength > 65536)
        {
            throw new InvalidDataException($"Frame body length {bodyLength} exceeds maximum.");
        }

        var fullLength = 1 + typeLength + 4 + (int)bodyLength;
        if (pending.Count < fullLength)
        {
            return false;
        }

        var typeInfo = Encoding.UTF8.GetString(bytes.AsSpan(1, typeLength));
        var body = bytes.AsSpan(1 + typeLength + 4, (int)bodyLength).ToArray();
        pending.RemoveRange(0, fullLength);

        frame = (typeInfo, body);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_cleanupTask is not null)
        {
            try
            {
                await _cleanupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }
}

internal sealed class ScaffoldingServerContext
{
    public ScaffoldingServerContext(string playerName, string machineId, string vendor, int minecraftServerPort, LobbyInfo lobbyInfo)
    {
        PlayerName = playerName;
        MinecraftServerPort = minecraftServerPort;
        UserLobbyInfo = lobbyInfo;
        var hostProfile = new PlayerProfile
        {
            Name = playerName,
            MachineId = machineId,
            Vendor = vendor,
            Kind = PlayerKind.HOST
        };
        TrackedPlayers.TryAdd(machineId, new TrackedPlayerProfile(hostProfile, DateTime.UtcNow));
    }

    public ConcurrentDictionary<string, TrackedPlayerProfile> TrackedPlayers { get; } = new();

    public IReadOnlyList<PlayerProfile> PlayerProfiles => TrackedPlayers.Values.Select(player => player.Profile).ToArray();

    public int MinecraftServerPort { get; }

    public LobbyInfo UserLobbyInfo { get; }

    public string PlayerName { get; }

    public event Action<IReadOnlyList<PlayerProfile>>? PlayerProfilesPing;

    public void OnPlayerProfilesChanged() => Task.Run(() => PlayerProfilesPing?.Invoke(PlayerProfiles));
}

internal sealed record TrackedPlayerProfile(PlayerProfile Profile, DateTime LastSeenUtc);
