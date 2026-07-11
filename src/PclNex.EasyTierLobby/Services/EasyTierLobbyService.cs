using System.Collections.Specialized;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Natayark;
using PCL.Core.Link.Scaffolding.EasyTier;
using PluginPlayerKind = PclNex.EasyTierLobby.Lobby.PlayerKind;
using PluginPlayerProfile = PclNex.EasyTierLobby.Lobby.PlayerProfile;

namespace PclNex.EasyTierLobby.Services;

internal sealed class EasyTierLobbyService : IAsyncDisposable, IDisposable
{
    private readonly IPluginLog _log;
    private readonly LobbyAnnouncementService _announcementService;
    private readonly NatayarkAuthService _natayark;
    private readonly EasyTierDownloader _downloader;
    private bool _disposed;

    public EasyTierLobbyService(string dataDirectory, IPluginLog? log = null)
    {
        DataDirectory = dataDirectory;
        _log = log ?? NullPluginLog.Instance;
        _announcementService = new LobbyAnnouncementService(_log);
        _natayark = new NatayarkAuthService(dataDirectory, _log);
        _downloader = new EasyTierDownloader(dataDirectory, _log);

        OriginalSecrets.ApplyToProcessEnvironment();

        LobbyService.StateChanged += OnLobbyStateChanged;
        LobbyService.Players.CollectionChanged += OnPlayersCollectionChanged;
        LobbyService.OnServerStarted += OnServerStarted;
        LobbyService.OnServerShutDown += OnServerShutDown;
        LobbyService.OnServerException += OnServerException;
        LobbyService.OnUserStopGame += OnUserStopGame;
    }

    public string DataDirectory { get; }

    public string EasyTierDirectory => EasyTierMetadata.EasyTierFilePath;

    public bool IsInLobby => LobbyService.CurrentState is LobbyState.Connected or LobbyState.Creating or LobbyState.Joining;

    public event Action<string>? StatusChanged;

    public event Action<IReadOnlyList<PluginPlayerProfile>>? PlayersChanged;

    public event Action<NatayarkProfile>? NatayarkProfileChanged;

    public LobbyAnnouncement LobbyAnnouncement => _announcementService.Current;

    public NatayarkProfile NatayarkProfile => _natayark.Profile;

    public static IReadOnlyList<FoundWorld> FindWorlds()
    {
        var discovered = LobbyService.DiscoveredWorlds
            .Select(world => new FoundWorld(world.Name, world.Port))
            .ToArray();

        return discovered.Length > 0 ? discovered : MinecraftWorldDetector.FindJavaPorts();
    }

    public bool HasEasyTierFiles() => EasyTierProcess.IsComplete(EasyTierDirectory);

    public async Task InitializeOriginalAsync(CancellationToken cancellationToken = default)
    {
        OriginalSecrets.ApplyToProcessEnvironment();
        StatusChanged?.Invoke("正在加载大厅配置...");

        var announcement = await _announcementService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        ApplyAnnouncementToCeCore(announcement);

        await SyncStoredNatayarkProfileAsync(cancellationToken).ConfigureAwait(false);
        await LobbyService.InitializeAsync().ConfigureAwait(false);

        StatusChanged?.Invoke(announcement.Available ? "大厅服务可用。" : "大厅服务暂不可用。");
        PlayersChanged?.Invoke(GetCurrentPlayers());
    }

    public async Task<NatayarkProfile> LoginNatayarkAsync(CancellationToken cancellationToken = default)
    {
        OriginalSecrets.ApplyToProcessEnvironment();
        var profile = await _natayark.LoginWithBrowserAsync(cancellationToken).ConfigureAwait(false);
        await SyncNatayarkProfileToCeCoreAsync(profile, cancellationToken).ConfigureAwait(false);
        NatayarkProfileChanged?.Invoke(profile);
        return profile;
    }

    public void LogoutNatayark()
    {
        _natayark.Logout();
        PCL.Core.App.States.Link.NaidRefreshToken = string.Empty;
        PCL.Core.App.States.Link.NaidRefreshExpireTime = string.Empty;
        NatayarkProfileChanged?.Invoke(_natayark.Profile);
    }

    public async Task<string> CreateLobbyAsync(string username, int minecraftPort, CancellationToken cancellationToken = default)
    {
        username = await PrecheckAndGetUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        ValidateMinecraftPort(minecraftPort);

        await LeaveAsync().ConfigureAwait(false);
        StatusChanged?.Invoke("正在创建大厅...");

        if (!await LobbyService.CreateLobbyAsync(minecraftPort, username).ConfigureAwait(false))
        {
            throw new InvalidOperationException("创建大厅失败。");
        }

        var code = LobbyService.CurrentLobbyCode ?? throw new InvalidOperationException("大厅已创建，但未返回大厅编号。");
        StatusChanged?.Invoke($"大厅已创建：{code}");
        PlayersChanged?.Invoke(GetCurrentPlayers());
        return code;
    }

    public async Task JoinLobbyAsync(string username, string lobbyCode, CancellationToken cancellationToken = default)
    {
        username = await PrecheckAndGetUsernameAsync(username, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(lobbyCode))
        {
            throw new ArgumentException("大厅编号不能为空。", nameof(lobbyCode));
        }

        await LeaveAsync().ConfigureAwait(false);
        StatusChanged?.Invoke("正在加入大厅...");

        if (!await LobbyService.JoinLobbyAsync(lobbyCode, username).ConfigureAwait(false))
        {
            throw new InvalidOperationException("加入大厅失败。");
        }

        StatusChanged?.Invoke("已加入大厅。");
        PlayersChanged?.Invoke(GetCurrentPlayers());
    }

    public async Task LeaveAsync()
    {
        await LobbyService.LeaveLobbyAsync().ConfigureAwait(false);
        StatusChanged?.Invoke("未连接大厅。");
        PlayersChanged?.Invoke([]);
    }

    public async Task EnsureEasyTierAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (EasyTierProcess.IsExplicitEasyTierDirectoryConfigured())
        {
            throw new InvalidOperationException($"PCLNEX_EASYTIER_DIR 指定的 EasyTier 目录缺少文件：{EasyTierDirectory}");
        }

        await _downloader.EnsureAsync(EasyTierDirectory, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncStoredNatayarkProfileAsync(CancellationToken cancellationToken)
    {
        if (!_natayark.HasRefreshToken)
        {
            return;
        }

        try
        {
            var profile = await _natayark.RefreshAsync(cancellationToken).ConfigureAwait(false);
            await SyncNatayarkProfileToCeCoreAsync(profile, cancellationToken).ConfigureAwait(false);
            NatayarkProfileChanged?.Invoke(profile);
        }
        catch (Exception ex)
        {
            _log.Warn($"Natayark refresh failed: {ex.Message}");
        }
    }

    private static async Task SyncNatayarkProfileToCeCoreAsync(NatayarkProfile profile, CancellationToken cancellationToken)
    {
        PCL.Core.App.States.Link.NaidRefreshToken = profile.RefreshToken;
        if (!string.IsNullOrWhiteSpace(profile.RefreshToken))
        {
            await NatayarkProfileManager.GetNaidDataAsync(profile.RefreshToken, isRefresh: true)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ApplyAnnouncementToCeCore(LobbyAnnouncement announcement)
    {
        LobbyInfoProvider.IsLobbyAvailable = announcement.Available;
        LobbyInfoProvider.AllowCustomName = announcement.AllowCustomName;
        LobbyInfoProvider.RequiresLogin = announcement.RequireLogin;
        LobbyInfoProvider.RequiresRealName = announcement.RequireRealname;
        LobbyInfoProvider.ProtocolVersion = (int)Math.Round(announcement.Version);
        ETRelay.RelayList = announcement.Relays
            .Where(relay => !string.IsNullOrWhiteSpace(relay.Url))
            .Select(relay => new ETRelay
            {
                Name = string.IsNullOrWhiteSpace(relay.Name) ? "Relay" : relay.Name,
                Url = relay.Url,
                Type = relay.Type.Equals("official", StringComparison.OrdinalIgnoreCase) ||
                       relay.Type.Equals("selfhosted", StringComparison.OrdinalIgnoreCase)
                    ? ETRelayType.Selfhosted
                    : ETRelayType.Community
            })
            .ToList();
    }

    private async Task<string> PrecheckAndGetUsernameAsync(string requestedUsername, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!LobbyInfoProvider.IsLobbyAvailable)
        {
            throw new InvalidOperationException("大厅功能暂不可用，请稍后再试。");
        }

        ValidateUsername(requestedUsername);

        if (!LobbyInfoProvider.RequiresLogin)
        {
            PCL.Core.App.Config.Link.Username = requestedUsername;
            return requestedUsername;
        }

        if (string.IsNullOrWhiteSpace(PCL.Core.App.States.Link.NaidRefreshToken))
        {
            throw new InvalidOperationException("请先登录 Natayark 账户。");
        }

        var profile = NatayarkProfileManager.NaidProfile;
        if (LobbyInfoProvider.RequiresRealName && !profile.IsRealNamed)
        {
            throw new InvalidOperationException("Natayark 账户需要完成实名后才能使用大厅。");
        }

        if (profile.Status != 0)
        {
            throw new InvalidOperationException("Natayark 账户状态异常，无法使用大厅。");
        }

        var username = LobbyInfoProvider.AllowCustomName && !string.IsNullOrWhiteSpace(requestedUsername)
            ? requestedUsername
            : profile.Username ?? requestedUsername;

        await Task.CompletedTask.ConfigureAwait(false);
        ValidateUsername(username);
        PCL.Core.App.Config.Link.Username = username;
        return username;
    }

    private static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("用户名不能为空。", nameof(username));
        }

        if (username.Contains('|'))
        {
            throw new ArgumentException("用户名不能包含 | 字符。", nameof(username));
        }
    }

    private static void ValidateMinecraftPort(int minecraftPort)
    {
        if (minecraftPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(minecraftPort), "Minecraft port must be 1-65535.");
        }
    }

    private void OnLobbyStateChanged(LobbyState oldState, LobbyState newState)
    {
        StatusChanged?.Invoke(newState switch
        {
            LobbyState.Initializing => "正在初始化大厅...",
            LobbyState.Discovering => "正在搜索局域网世界...",
            LobbyState.Creating => "正在创建大厅...",
            LobbyState.Joining => "正在加入大厅...",
            LobbyState.Connected => "大厅已连接。",
            LobbyState.Leaving => "正在退出大厅...",
            LobbyState.Error => "大厅状态异常。",
            _ => "未连接大厅。"
        });
    }

    private void OnPlayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => PlayersChanged?.Invoke(GetCurrentPlayers());

    private void OnServerStarted() => PlayersChanged?.Invoke(GetCurrentPlayers());

    private void OnServerShutDown() => StatusChanged?.Invoke("大厅服务已断开。");

    private void OnServerException(Exception ex) => StatusChanged?.Invoke($"大厅服务异常：{ex.Message}");

    private void OnUserStopGame() => StatusChanged?.Invoke("检测到本地游戏已关闭，大厅已退出。");

    private static IReadOnlyList<PluginPlayerProfile> GetCurrentPlayers()
        => LobbyService.Players.Select(ToPluginPlayer).ToArray();

    private static PluginPlayerProfile ToPluginPlayer(PCL.Core.Link.Scaffolding.Client.Models.PlayerProfile player)
        => new()
        {
            Name = player.Name,
            MachineId = player.MachineId,
            Vendor = player.Vendor,
            Kind = player.Kind is null ? null : Enum.Parse<PluginPlayerKind>(player.Kind.Value.ToString())
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        LobbyService.StateChanged -= OnLobbyStateChanged;
        LobbyService.Players.CollectionChanged -= OnPlayersCollectionChanged;
        LobbyService.OnServerStarted -= OnServerStarted;
        LobbyService.OnServerShutDown -= OnServerShutDown;
        LobbyService.OnServerException -= OnServerException;
        LobbyService.OnUserStopGame -= OnUserStopGame;
        LeaveAsync().GetAwaiter().GetResult();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}