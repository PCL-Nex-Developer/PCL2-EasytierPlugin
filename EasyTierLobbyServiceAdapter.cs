using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.EasyTierPlugin.Natayark;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.Utils;
using PCL.Core.Utils.Exts;
using PCL.EasyTierPlugin.Lobby;
using PCL.EasyTierPlugin.Scaffolding.Client.Models;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

internal sealed class EasyTierLobbyServiceAdapter : ILobbyService
{
    public EasyTierLobbyServiceAdapter()
    {
        SyncWorlds();
        SyncPlayers();
        LobbyService.DiscoveredWorlds.CollectionChanged += OnWorldsChanged;
        LobbyService.Players.CollectionChanged += OnPlayersChanged;
        LobbyService.StateChanged += (oldState, newState) => StateChanged?.Invoke(Map(oldState), Map(newState));
        LobbyService.OnUserStopGame += () => OnUserStopGame?.Invoke();
        LobbyService.OnClientPing += latency => OnClientPing?.Invoke(latency);
        LobbyService.OnServerShutDown += () => OnServerShutDown?.Invoke();
        LobbyService.OnServerStarted += () => OnServerStarted?.Invoke();
        LobbyService.OnServerException += ex => OnServerException?.Invoke(ex);
    }

    public ObservableCollection<LobbyFoundWorld> DiscoveredWorlds { get; } = [];
    public ObservableCollection<LobbyPlayerProfile> Players { get; } = [];
    public LobbyServiceState CurrentState => Map(LobbyService.CurrentState);
    public bool IsHost => LobbyService.IsHost;
    public bool IsLobbyAvailable { get => LobbyInfoProvider.IsLobbyAvailable; set => LobbyInfoProvider.IsLobbyAvailable = value; }
    public bool AllowCustomName { get => LobbyInfoProvider.AllowCustomName; set => LobbyInfoProvider.AllowCustomName = value; }
    public bool RequiresLogin { get => LobbyInfoProvider.RequiresLogin; set => LobbyInfoProvider.RequiresLogin = value; }
    public bool RequiresRealName { get => LobbyInfoProvider.RequiresRealName; set => LobbyInfoProvider.RequiresRealName = value; }
    public int ProtocolVersion => LobbyInfoProvider.ProtocolVersion;
    public string? CurrentLobbyCode => LobbyService.CurrentLobbyCode;
    public string? CurrentUserName => LobbyService.CurrentUserName;
    public int? LocalMinecraftPort => LobbyInfoProvider.McForward?.LocalPort;
    public LobbySettingsSnapshot Settings => EasyTierPluginSettings.Snapshot;
    public bool IsEulaAccepted { get => EasyTierPluginState.IsEulaAccepted; set => EasyTierPluginState.IsEulaAccepted = value; }
    public bool HasAccountLogin => !string.IsNullOrWhiteSpace(EasyTierPluginState.NaidRefreshToken);
    public string? AccountDisplayName => NatayarkProfileManager.NaidProfile.Username;

    public event Action<LobbyServiceState, LobbyServiceState>? StateChanged;
    public event Action? OnUserStopGame;
    public event Action<long>? OnClientPing;
    public event Action? OnServerShutDown;
    public event Action? OnServerStarted;
    public event Action<Exception>? OnServerException;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LobbyService.InitializeAsync().ConfigureAwait(false);
    }

    public async Task DiscoverWorldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LobbyService.DiscoverWorldAsync().ConfigureAwait(false);
    }

    public async Task<bool> CreateLobbyAsync(int port, string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await LobbyService.CreateLobbyAsync(port, username).ConfigureAwait(false);
    }

    public async Task<bool> JoinLobbyAsync(string lobbyCode, string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await LobbyService.JoinLobbyAsync(lobbyCode, username).ConfigureAwait(false);
    }

    public async Task LeaveLobbyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await LobbyService.LeaveLobbyAsync().ConfigureAwait(false);
    }

    public string? GetUsername() => LobbyInfoProvider.GetUsername();

    public bool Precheck(string? selectedProfileUsername)
    {
        if (!IsLobbyAvailable)
        {
            HintWrapper.Show(Lang.Text("Link.Mod.LobbyUnavailable"), HintTheme.Error);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selectedProfileUsername) && selectedProfileUsername.Contains('|'))
        {
            HintWrapper.Show(Lang.Text("Link.Mod.InvalidPlayerId"), HintTheme.Info);
            return false;
        }

        if (RequiresLogin)
        {
            if (string.IsNullOrWhiteSpace(EasyTierPluginState.NaidRefreshToken))
            {
                HintWrapper.Show(Lang.Text("Link.Mod.LoginFirst"), HintTheme.Error);
                return false;
            }

            try
            {
                NatayarkProfileManager.GetNaidDataAsync(EasyTierPluginState.NaidRefreshToken, true)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogWrapper.Warn(ex, "Link", "刷新 Natayark ID 信息失败，需要重新登录");
                HintWrapper.Show(Lang.Text("Link.Mod.ReLoginRequired"), HintTheme.Error);
                return false;
            }

            var waitCount = 0;
            while (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
            {
                if (waitCount > 30) break;
                Thread.Sleep(500);
                waitCount++;
            }

            if (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
            {
                HintWrapper.Show(Lang.Text("Link.Mod.NaidFetchFailed"), HintTheme.Error);
                return false;
            }

            if (RequiresRealName && !NatayarkProfileManager.NaidProfile.IsRealNamed)
            {
                HintWrapper.Show(Lang.Text("Link.Mod.RealNameRequired"), HintTheme.Error);
                return false;
            }

            if (NatayarkProfileManager.NaidProfile.Status != 0)
            {
                HintWrapper.Show(Lang.Text("Link.Mod.AccountBanned"), HintTheme.Error);
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(EasyTierPluginSettings.Username) && string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
        {
            HintWrapper.Show(Lang.Text("Link.Mod.UsernameOrLogin"), HintTheme.Error);
            return false;
        }

        return true;
    }

    public void UpdateSettings(LobbySettingsSnapshot settings) => EasyTierPluginSettings.Update(settings);

    public void ResetSettings() => EasyTierPluginSettings.Reset();

    public string GetAnnouncementCache() => EasyTierPluginState.AnnouncementCache;

    public int GetAnnouncementCacheVersion() => EasyTierPluginState.AnnouncementCacheVersion;

    public void SetAnnouncementCache(string cache, int version)
    {
        EasyTierPluginState.AnnouncementCache = cache;
        EasyTierPluginState.AnnouncementCacheVersion = version;
    }

    public void ResetAnnouncementCache() => EasyTierPluginState.ResetAnnouncementCache();

    public async Task RefreshAccountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expireTime = EasyTierPluginState.NaidRefreshExpireTime;
        if (!string.IsNullOrWhiteSpace(expireTime) && Convert.ToDateTime(expireTime).CompareTo(DateTime.Now) < 0)
        {
            EasyTierPluginState.NaidRefreshToken = string.Empty;
            HintWrapper.Show(Lang.Text("Tools.GameLink.Natayark.TokenExpired"), HintTheme.Error);
            return;
        }

        await NatayarkProfileManager.GetNaidDataAsync(EasyTierPluginState.NaidRefreshToken, true).ConfigureAwait(false);
    }

    public Task StartAccountLoginAsync(Action? completeCallback = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NatayarkOAuthService.StartAuthorize(completeCallback);
        return Task.CompletedTask;
    }

    public void LogoutAccount()
    {
        EasyTierPluginState.ResetAccount();
        NatayarkProfileManager.ClearProfile();
    }

    public Task<LobbyAnnouncementSnapshot> FetchAnnouncementAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serverNumber = 0;
        JsonObject? jObj = null;
        var linkServers = EasyTierPluginSecrets.Get("LINK_SERVER_ROOT", readEnvDebugOnly: true)
            .ReplaceNullOrEmpty()
            .Split("|");

        while (serverNumber < linkServers.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var cacheRes = HttpRequest
                    .Create($"{linkServers[serverNumber]}/api/link/v2/cache.ini")
                    .SendAsync()
                    .GetAwaiter()
                    .GetResult()
                    .AsStringAsync()
                    .GetAwaiter()
                    .GetResult()
                    .Trim();
                var cacheVer = int.Parse(cacheRes);

                if (cacheVer == EasyTierPluginState.AnnouncementCacheVersion)
                {
                    LogWrapper.Info("[Link] Using cached announcement data");
                    jObj = (JsonObject)JsonCompat.ParseNode(EasyTierPluginState.AnnouncementCache)!;
                }
                else
                {
                    LogWrapper.Info("[Link] Fetching new announcement data");
                    var received = HttpRequest
                        .Create($"{linkServers[serverNumber]}/api/link/v2/announce.json")
                        .SendAsync()
                        .GetAwaiter()
                        .GetResult()
                        .AsStringAsync()
                        .GetAwaiter()
                        .GetResult();
                    jObj = (JsonObject)JsonCompat.ParseNode(received)!;
                    EasyTierPluginState.AnnouncementCache = received;
                    EasyTierPluginState.AnnouncementCacheVersion = cacheVer;
                }

                break;
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, $"[Link] Failed to get announcement from server {serverNumber}");
                EasyTierPluginState.ResetAnnouncementCache();
                serverNumber++;
            }
        }

        if (jObj is null) throw new Exception("Failed to fetch lobby data");

        var notices = new List<LobbyAnnouncementNotice>();
        foreach (JsonObject notice in (JsonArray)jObj["notices"]!)
        {
            var content = notice["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content)) continue;
            notices.Add(new LobbyAnnouncementNotice
            {
                Content = content,
                Type = notice["type"]?.ToString() ?? "notice",
                MinVersion = notice["minVer"]?.GetValue<double>() ?? 0,
                MaxVersion = notice["maxVer"]?.GetValue<double>() ?? double.MaxValue
            });
        }

        var relays = new List<LobbyRelayDescriptor>();
        foreach (JsonObject relay in (JsonArray)jObj["relays"]!)
        {
            relays.Add(new LobbyRelayDescriptor
            {
                Name = relay["name"]?.ToString() ?? string.Empty,
                Url = relay["url"]?.ToString() ?? string.Empty,
                Type = relay["type"]?.ToString() == "official" ? LobbyRelayKind.Selfhosted : LobbyRelayKind.Community
            });
        }

        return Task.FromResult(new LobbyAnnouncementSnapshot
        {
            Available = jObj["available"]?.GetValue<bool>() ?? false,
            AllowCustomName = jObj["allowCustomName"]?.GetValue<bool>() ?? false,
            RequiresLogin = jObj["requireLogin"]?.GetValue<bool>() ?? true,
            RequiresRealName = jObj["requireRealname"]?.GetValue<bool>() ?? true,
            Version = jObj["version"]?.GetValue<double>() ?? 0,
            Notices = notices,
            Relays = relays
        });
    }

    public void SetRelayServers(IEnumerable<LobbyRelayDescriptor> relays)
    {
        LobbyRelay.RelayList = relays.Select(static relay => new LobbyRelay
        {
            Name = relay.Name,
            Url = relay.Url,
            Type = relay.Type switch
            {
                LobbyRelayKind.Selfhosted => LobbyRelayType.Selfhosted,
                LobbyRelayKind.Custom => LobbyRelayType.Custom,
                _ => LobbyRelayType.Community
            }
        }).ToList();
    }

    private void OnWorldsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncWorlds();
    private void OnPlayersChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncPlayers();

    private void SyncWorlds()
    {
        DiscoveredWorlds.Clear();
        foreach (var world in LobbyService.DiscoveredWorlds)
        {
            DiscoveredWorlds.Add(new LobbyFoundWorld(world.Name, world.Port));
        }
        RaiseReset(DiscoveredWorlds);
    }

    private void SyncPlayers()
    {
        Players.Clear();
        foreach (var player in LobbyService.Players)
        {
            Players.Add(ToDto(player));
        }
        RaiseReset(Players);
    }

    private static void RaiseReset<T>(ObservableCollection<T> collection)
    {
        var method = typeof(ObservableCollection<T>).GetMethod("OnCollectionChanged", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(NotifyCollectionChangedEventArgs)]);
        method?.Invoke(collection, [new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)]);
    }

    private static LobbyPlayerProfile ToDto(PlayerProfile profile) => new()
    {
        Name = profile.Name,
        MachineId = profile.MachineId,
        Vendor = profile.Vendor,
        Kind = profile.Kind switch
        {
            PlayerKind.HOST => LobbyPlayerKind.HOST,
            PlayerKind.GUEST => LobbyPlayerKind.GUEST,
            _ => null
        }
    };

    private static LobbyServiceState Map(LobbyState state) => state switch
    {
        LobbyState.Initializing => LobbyServiceState.Initializing,
        LobbyState.Initialized => LobbyServiceState.Initialized,
        LobbyState.Discovering => LobbyServiceState.Discovering,
        LobbyState.Creating => LobbyServiceState.Creating,
        LobbyState.Joining => LobbyServiceState.Joining,
        LobbyState.Connected => LobbyServiceState.Connected,
        LobbyState.Leaving => LobbyServiceState.Leaving,
        LobbyState.Error => LobbyServiceState.Error,
        _ => LobbyServiceState.Idle
    };
}
