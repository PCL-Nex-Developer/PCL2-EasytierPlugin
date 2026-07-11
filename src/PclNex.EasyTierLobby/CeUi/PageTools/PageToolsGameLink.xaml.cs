using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.Link;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.McPing;
using PCL.Core.Link.Natayark;
using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;
using PCL.Core.Utils.Validate;
using PCL.Network;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL;

namespace PclNex.EasyTierLobby.UI;

public partial class PageToolsGameLink
{
    private static PageToolsGameLink? _currentPage;
    private bool _lobbyEventsSubscribed;
    private int _createInProgress;
    private int _joinInProgress;

    static PageToolsGameLink()
    {
        initLoader = new ModLoader.LoaderCombo<int>(Lang.Text("Link.Mod.Task.InitLobby"),
            new[] { new ModLoader.LoaderTask<int, int>(Lang.Text("Common.Action.Initialize"), InitTask) { ProgressWeight = 0.5d } });
    }

    public PageToolsGameLink()
    {
        ModBase.Log($"[Link] 插件工具页已实例化：{GetType().AssemblyQualifiedName}");
        _currentPage = this;
        InitializeComponent();
        BindActionHandlers();
        LoaderInit();
        Loaded += (_, _) =>
        {
            SubscribeLobbyEvents();
            RestoreLobbyUiFromState();
            Reload();
        };
        Unloaded += (_, _) => UnsubscribeLobbyEvents();
        PageEnter += PageLinkLobby_OnPageEnter;
    }

    private void BindActionHandlers()
    {
        BtnNatTest.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(BtnNetTest_Click), handledEventsToo: true);
        BtnJoin.Click += BtnJoin_Click;
        BtnPaste.Click += PasteLobbyId;
        BtnClearLobbyId.Click += ClearLobbyId;
        BtnCreate.Click += BtnCreate_Click;
        BtnRefresh.Click += BtnRefresh_Click;
        BtnInputPort.Click += BtnInputPort_Click;
        BtnEulaAgree.Click += BtnAgreeEula_Click;
        BtnEulaStop.Click += BtnEulaStop_Click;
        BtnLoadCancel.Click += CancelLoad;
        BtnFinishCopyIp.Click += BtnFinishCopyIp_Click;
        BtnFinishCopy.Click += BtnFinishCopy_Click;
        BtnFinishExit.Click += BtnFinishExit_Click;
        ModBase.Log("[Link] 联机操作按钮 Click 事件已绑定");
    }

    #region 初始化

    // 加载器初始化
    private void LoaderInit()
    {
        PageLoaderInit(Load, PanLoad, PanContent, null, initLoader, autoRun: false);
        // 注册自定义的 OnStateChanged
        initLoader.OnStateChangedUi += OnLoadStateChanged;

        if (lobbyAnnouncementLoader is null)
        {
            var loaders = new List<ModLoader.LoaderBase>();
            loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.InitLobbyUi"), _ => ModBase.RunInUi(() =>
            {
                HintAnnounce.Visibility = Visibility.Visible;
                HintAnnounce.Theme = MyHint.Themes.Blue;
                HintAnnounce.Text = "正在加载大厅配置...";
            })));
            loaders.Add(new ModLoader.LoaderTask<int, int>(Lang.Text("Link.Mod.Task.FetchAnnouncement"), _ => GetAnnouncement()) { ProgressWeight = 0.5d });
            lobbyAnnouncementLoader = new ModLoader.LoaderCombo<int>("Lobby Announcement", loaders) { show = false };
        }
    }

    private void SubscribeLobbyEvents()
    {
        if (_lobbyEventsSubscribed)
            return;

        _lobbyEventsSubscribed = true;
        LobbyService.OnNeedDownloadEasyTier += OnNeedDownloadEasyTier;
        LobbyService.DiscoveredWorlds.CollectionChanged += OnDiscoveredWorldsChanged;
        LobbyService.Players.CollectionChanged += OnPlayersChanged;
        LobbyService.OnUserStopGame += OnUserStopGame;
        LobbyService.OnClientPing += OnClientPingHandler;
        LobbyService.OnServerShutDown += OnServerShuttedDownHandler;
        LobbyService.OnServerStarted += OnServerStartedHandler;
        LobbyService.OnServerException += OnServerExceptionHandler;
        Config.Link.UsernameChanged += OnUsernameChanged;
        OnUsernameChanged(Config.Link.Username);
    }

    private void UnsubscribeLobbyEvents()
    {
        if (!_lobbyEventsSubscribed)
            return;

        _lobbyEventsSubscribed = false;
        LobbyService.OnNeedDownloadEasyTier -= OnNeedDownloadEasyTier;
        LobbyService.DiscoveredWorlds.CollectionChanged -= OnDiscoveredWorldsChanged;
        LobbyService.Players.CollectionChanged -= OnPlayersChanged;
        LobbyService.OnUserStopGame -= OnUserStopGame;
        LobbyService.OnClientPing -= OnClientPingHandler;
        LobbyService.OnServerShutDown -= OnServerShuttedDownHandler;
        LobbyService.OnServerStarted -= OnServerStartedHandler;
        LobbyService.OnServerException -= OnServerExceptionHandler;
        Config.Link.UsernameChanged -= OnUsernameChanged;
    }

    private void OnUsernameChanged(string username)
    {
        ModBase.RunInUi(() =>
        {
            LabConnectUserName.Text = username;
        });
    }

    private void RestoreLobbyUiFromState()
    {
        if (LobbyService.CurrentState != LobbyState.Connected)
            return;

        LabFinishId.Text = LobbyService.CurrentLobbyCode ?? string.Empty;
        LabConnectUserName.Text = LobbyService.CurrentUserName ?? string.Empty;
        LabConnectUserType.Text = Lang.Text(LobbyService.IsHost
            ? "Tools.GameLink.Finish.Host"
            : "Tools.GameLink.Finish.Guest");
        BtnFinishExit.Text = Lang.Text(LobbyService.IsHost
            ? "Tools.GameLink.Finish.CloseLobby"
            : "Tools.GameLink.Finish.Exit");
        BtnFinishCopyIp.Visibility = LobbyService.IsHost ? Visibility.Collapsed : Visibility.Visible;
        StackPlayerList.Children.Clear();
        foreach (var player in LobbyService.Players)
            StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
        CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListCount", LobbyService.Players.Count);
        CurrentSubpage = Subpages.PanFinish;
        ModBase.Log("[Link] 已从联机服务恢复已连接大厅页面");
    }

    private static bool IsNatayarkLoggedIn() =>
        !string.IsNullOrWhiteSpace(States.Link.NaidRefreshToken) &&
        !string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username);

    private void RefreshIdentityUi()
    {
        var isLoggedIn = IsNatayarkLoggedIn();
        LabNatayarkUserName.Text = isLoggedIn
            ? NatayarkProfileManager.NaidProfile.Username
            : Lang.Text("Tools.GameLink.Natayark.Login");
        LabNatayarkUserName.Opacity = isLoggedIn ? 1.0 : 0.8;
    }

    private static void OnNeedDownloadEasyTier() => ModLink.DownloadEasyTier();

    private async void OnServerExceptionHandler(Exception ex)
    {
        ModBase.RunInUi(() => HintService.Hint(
            Lang.Text("Tools.GameLink.Error.ServerMessage", ex.Message),
            HintType.Error));

        try
        {
            await LobbyService.LeaveLobbyAsync();
            await RefreshLocalWorldsAsync();

            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
        }
        catch (Exception secEx)
        {
            ModBase.Log(secEx, "Occurred an exception when exit server.");
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerExit"), HintType.Error);
        }
    }

    public async void Reload()
    {
        if (States.Link.LinkEula)
            StartAnnouncements();
        else
            StopAnnouncements();

        await LobbyService.InitializeAsync().ConfigureAwait(false);
    }

    private void StartAnnouncements()
    {
        _linkAnnounceUpdateCancelSource?.Cancel();
        HintAnnounce.Visibility = Visibility.Visible;
        HintAnnounce.Text = Lang.Text("Tools.GameLink.Loading.ConnectingServer");
        HintAnnounce.Theme = MyHint.Themes.Blue;

        if (_linkAnnounces.Count == 0)
            GetAnnouncement();

        _linkAnnounceUpdateCancelSource = new CancellationTokenSource();
        _ = Dispatcher.BeginInvoke(new Action(async () =>
            await _LinkAnnounceUpdateAsync()));
    }

    private void StopAnnouncements()
    {
        _linkAnnounceUpdateCancelSource?.Cancel();
        _linkAnnounceUpdateCancelSource = null;
        HintAnnounce.Visibility = Visibility.Collapsed;
    }

    private void BtnAgreeEula_Click(object sender, MouseButtonEventArgs e)
    {
        States.Link.LinkEula = true;
        CurrentSubpage = Subpages.PanSelect;
        StartAnnouncements();
    }

    private void BtnEulaStop_Click(object sender, EventArgs eventArgs)
    {
        if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Eula.RevokeConfirm"),
                Lang.Text("Tools.GameLink.Eula.RevokeTitle"),
                Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel"),
                isWarn: true
            ) == 1)
        {
            States.Link.NaidRefreshTokenConfig.Reset();
            States.Link.LinkEula = false;
            StopAnnouncements();
            HintService.Hint(Lang.Text("Tools.GameLink.Eula.Disabled"));
            CurrentSubpage = Subpages.PanEula;
        }
    }

    private static readonly ModLoader.LoaderCombo<int> initLoader;

    private static async void InitTask(ModLoader.LoaderTask<int, int> task)
    {
        await LobbyService.InitializeAsync();
    }

    #region Subscribser

    private void OnServerStartedHandler()
    {
        ModBase.Log("Received server started event.");
        ModBase.RunInUi(() =>
        {
            LabFinishId.Text = LobbyService.CurrentLobbyCode;
            StackPlayerList.Children.Clear();
            foreach (var player in LobbyService.Players)
                StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
        });
    }

    private async void OnServerShuttedDownHandler()
    {
        try
        {
            await LobbyService.LeaveLobbyAsync();

            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                CurrentSubpage = Subpages.PanSelect;
            });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "Occurred an exception when exit server.");
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerExit"), HintType.Error);
        }
    }

    private void OnClientPingHandler(long latency)
    {
        ModBase.RunInUi(() =>
        {
            LabFinishQuality.Text = Lang.Text("Tools.GameLink.Finish.Connected");
            LabFinishPing.Text = Lang.Text("Tools.GameLink.Finish.PingMs", latency);
            LabConnectType.Text = LobbyTextHandler.GetConnectTypeName(LobbyService.CurrentConnectionWay);
        });
    }

    private async void OnUserStopGame()
    {
        await LobbyService.LeaveLobbyAsync();
        await RefreshLocalWorldsAsync();

        ModBase.RunInUi(() =>
        {
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
            StackPlayerList.Children.Clear();
            CurrentSubpage = Subpages.PanSelect;
        });
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Exit.Disbanded"), Lang.Text("Tools.GameLink.Exit.DisbandedTitle"));
    }


    private void OnPlayersChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        ModBase.Log("接收到玩家列表改变事件");
        ModBase.RunInUi(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is not null)
                        foreach (PlayerProfile player in e.NewItems)
                            StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is not null)
                        foreach (PlayerProfile player in e.OldItems)
                        {
                            var itemToRemove = StackPlayerList.Children.OfType<MyListItem>()
                                .FirstOrDefault(item => ((PlayerProfile)item.Tag).MachineId == player.MachineId);
                            if (itemToRemove is not null) StackPlayerList.Children.Remove(itemToRemove);
                        }

                    break;
                default:
                    StackPlayerList.Children.Clear();
                    foreach (var player in LobbyService.Players)
                        StackPlayerList.Children.Add((UIElement)PlayerInfoItem(player, PlayerInfoClick));
                    break;
            }

            LabFinishQuality.Text = Lang.Text("Tools.GameLink.Finish.Connected");
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListCount", LobbyService.Players.Count);
        });
    }


    private void OnDiscoveredWorldsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        LogWrapper.Info("[Lobby] Found new world changes");

        ModBase.RunInUi(() =>
        {
            #region 处理集合变更

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    ComboWorldList.Items.Clear();
                    foreach (var world in LobbyService.DiscoveredWorlds)
                        ComboWorldList.Items.Add(new MyComboBoxItem
                        {
                            Tag = world.Port,
                            Content = world.Name
                        });
                    break;

                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems is not null)
                        foreach (FoundWorld world in e.NewItems)
                            ComboWorldList.Items.Add(new MyComboBoxItem
                            {
                                Tag = world.Port,
                                Content = world.Name
                            });

                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems is not null)
                    {
                        // 使用 HashSet 提高查询效率
                        var portsToRemove = e.OldItems.Cast<FoundWorld>().Select(w => w.Port).ToHashSet();
                        var itemsToRemove = ComboWorldList.Items
                            .Cast<MyComboBoxItem>()
                            .Where(item => portsToRemove.Contains((int)item.Tag))
                            .ToList();

                        foreach (var item in itemsToRemove) ComboWorldList.Items.Remove(item);
                    }

                    break;
            }

            #endregion

            #region 更新 UI 状态

            var hasItems = ComboWorldList.Items.Count > 0;
            ComboWorldList.IsEnabled = hasItems;
            BtnCreate.IsEnabled = true;

            if (hasItems && ComboWorldList.SelectedIndex == -1) ComboWorldList.SelectedIndex = 0;

            #endregion
        });
    }

    #endregion

    #endregion

    #region 公告

    public static ModLoader.LoaderCombo<int> lobbyAnnouncementLoader;
    private readonly ObservableCollection<LinkAnnounceInfo> _linkAnnounces = new();

    private CancellationTokenSource _linkAnnounceUpdateCancelSource;

    // 公告轮播实现
    private async Task _LinkAnnounceUpdateAsync()
    {
        var currentIndex = 0;
        var cancelSource = _linkAnnounceUpdateCancelSource;
        if (cancelSource is null)
            return;
        var globalCancelToken = cancelSource.Token;
        CancellationTokenSource waiterCts = null;

        _linkAnnounces.CollectionChanged += (sender, e) =>
        {
            if (waiterCts is not null) waiterCts.Cancel();
        };

        while (!globalCancelToken.IsCancellationRequested)
        {
            waiterCts = CancellationTokenSource.CreateLinkedTokenSource(globalCancelToken);
            var waiterCancelToken = waiterCts.Token;

            if (_linkAnnounces.Count > 0)
            {
                var info = _linkAnnounces[currentIndex];
                HintAnnounce.Visibility = Visibility.Visible;
                string prefix;
                if (info.Type == LinkAnnounceType.Important)
                {
                    HintAnnounce.Theme = MyHint.Themes.Red;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Important");
                }
                else if (info.Type == LinkAnnounceType.Warning)
                {
                    HintAnnounce.Theme = MyHint.Themes.Yellow;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Warning");
                }
                else
                {
                    HintAnnounce.Theme = MyHint.Themes.Blue;
                    prefix = Lang.Text("Tools.GameLink.Announcement.Notice");
                }

                HintAnnounce.Text = Lang.Text("Tools.GameLink.Announcement.Format", prefix,
                    info.Content.Replace("\n", "\r\n"));
            }
            else
            {
                HintAnnounce.Visibility = Visibility.Collapsed;
            }

            try
            {
                await Task.Delay(10000, waiterCancelToken);
            }
            catch (TaskCanceledException)
            {
                // 忽略取消任务的异常
            }

            if (!waiterCancelToken.IsCancellationRequested)
                currentIndex += 1;
            if (currentIndex >= _linkAnnounces.Count)
                currentIndex = 0;
            waiterCts = null;
        }
    }

    // 获取公告信息
    private void GetAnnouncement()
    {
        ModBase.RunInNewThread(() =>
        {
            try
            {
                var serverNumber = 0;
                JsonObject jObj = null;

                #region 多服务器轮询获取公告

                while (serverNumber < SecretsCompat.LinkServers.Length)
                    try
                    {
                        // 获取缓存版本号
                        var cacheRes = Requester.Fetch($"{SecretsCompat.LinkServers[serverNumber]}/api/link/v2/cache.ini",
                            new FetchParam
                            {
                                Method = "GET",
                                ContentType = "application/json",
                                Timeout = 7000
                            }).Trim();
                        var cacheVer = int.Parse(cacheRes);

                        if (cacheVer == States.Link.AnnounceCacheVer)
                        {
                            LogWrapper.Info("[Link] Using cached announcement data");
                            jObj = (JsonObject)ModBase.GetJson(States.Link.AnnounceCache);
                        }
                        else
                        {
                            LogWrapper.Info("[Link] Fetching new announcement data");
                            var received = Requester.Fetch(
                                $"{SecretsCompat.LinkServers[serverNumber]}/api/link/v2/announce.json",
                                new FetchParam
                                {
                                    Method = "GET",
                                    ContentType = "application/json",
                                    Timeout = 7000
                                });
                            jObj = (JsonObject)ModBase.GetJson(received);

                            // 更新缓存
                            States.Link.AnnounceCache = received;
                            States.Link.AnnounceCacheVer = cacheVer;
                        }

                        break; // 成功获取，跳出轮询
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.Error(ex, $"[Link] Failed to get announcement from server {serverNumber}");
                        States.Link.AnnounceCacheConfig.Reset();
                        States.Link.AnnounceCacheVerConfig.Reset();
                        serverNumber++;
                    }

                #endregion

                if (jObj is null) throw new Exception("Failed to fetch lobby data");

                #region 解析基础状态与版本限制

                LobbyInfoProvider.IsLobbyAvailable = (bool)jObj["available"];
                LobbyInfoProvider.AllowCustomName = (bool)jObj["allowCustomName"];
                LobbyInfoProvider.RequiresLogin = (bool)jObj["requireLogin"];
                LobbyInfoProvider.RequiresRealName = (bool)jObj["requireRealname"];

                if (jObj["version"].ToObject<double>() > LobbyInfoProvider.ProtocolVersion)
                {
                    ModBase.RunInUi(() =>
                    {
                        HintAnnounce.Theme = MyHint.Themes.Red;
                        HintAnnounce.Text = Lang.Text("Tools.GameLink.Error.UpdateRequired");
                        LobbyInfoProvider.IsLobbyAvailable = false;
                    });
                    return;
                }

                #endregion

                #region 解析公告列表 (Notices)

                var notices = (JsonArray)jObj["notices"];
                foreach (JsonObject notice in notices)
                {
                    var content = notice["content"]?.ToString();
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    // 版本过滤
                    var minVer = notice["minVer"].ToObject<double>();
                    var maxVer = notice["maxVer"].ToObject<double>();
                    if (ModBase.versionCode < minVer || maxVer < 999 && ModBase.versionCode > maxVer) continue;

                    // 类型映射
                    var type = LinkAnnounceType.Notice;
                    var typeStr = notice["type"]?.ToString().ToLower();
                    if (typeStr == "important" || typeStr == "red") type = LinkAnnounceType.Important;
                    else if (typeStr == "warning" || typeStr == "yellow") type = LinkAnnounceType.Warning;

                    // 按行拆分公告
                    foreach (var announce in content.Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(announce)) continue;
                        _linkAnnounces.Add(new LinkAnnounceInfo(type, announce));
                    }
                }

                LogWrapper.Info("Link", $"Loaded {_linkAnnounces.Count} lobby announcement item(s).");

                #endregion

                #region 解析中继服务器 (Relays)

                var relays = (JsonArray)jObj["relays"];
                ETRelay.RelayList = new List<ETRelay>();
                foreach (var relay in relays)
                    ETRelay.RelayList.Add(new ETRelay
                    {
                        Name = relay["name"]?.ToString(),
                        Url = relay["url"]?.ToString(),
                        Type = relay["type"]?.ToString() == "official" ? ETRelayType.Selfhosted : ETRelayType.Community
                    });

                #endregion

                #region 处理账户登录状态显示

                if (string.IsNullOrWhiteSpace(States.Link.NaidRefreshToken))
                {
                    ModBase.RunInUi(RefreshIdentityUi);
                }
                else
                {
                    ModBase.RunInUi(() => LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.Loading"));
                    if (string.IsNullOrEmpty(NatayarkProfileManager.NaidProfile.Username))
                        ReloadNaidData();
                    else
                        ModBase.RunInUi(() =>
                        {
                            if (NatayarkProfileManager.NaidProfile.Status == 0)
                            {
                                LabNatayarkUserName.Text = NatayarkProfileManager.NaidProfile.Username;
                                LabNatayarkUserName.Opacity = 1;
                            }
                            else
                            {
                                LabNatayarkUserName.Text = $"{NatayarkProfileManager.NaidProfile.Username} {Lang.Text("Tools.GameLink.Natayark.Abnormal")}";
                                LabNatayarkUserName.Opacity = 0.6;
                            }
                        });
                }

                #endregion
            }
            catch (Exception ex)
            {
                LobbyInfoProvider.IsLobbyAvailable = false;
                ModBase.RunInUi(() =>
                {
                    HintAnnounce.Theme = MyHint.Themes.Red;
                    HintAnnounce.Text = Lang.Text("Tools.GameLink.Error.ConnectFailed");
                });
                LogWrapper.Error(ex, "[Link] Failed to get lobby announcement");
            }
        });
    }

    #endregion

    #region 信息获取与展示

    #region UI 元素

    private object PlayerInfoItem(PlayerProfile info, MyListItem.ClickEventHandler onClick)
    {
        var details = info.Kind == PlayerKind.HOST
            ? Lang.Text("Tools.GameLink.Player.Details", Lang.Text("Tools.GameLink.Player.Host"), info.Vendor)
            : info.Vendor;

        var newItem = new MyListItem
        {
            Title = info.Name,
            Info = details,
            Type = MyListItem.CheckType.Clickable,
            Tag = info
        };
        newItem.Click += onClick;

        return newItem;
    }

    private void PlayerInfoClick(object sender, MouseButtonEventArgs e)
    {
        var info = (PlayerProfile)((MyListItem)sender).Tag;
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Player.InfoMessage", info.Name, info.Vendor), Lang.Text("Tools.GameLink.Player.InfoTitle", info.Name));
    }

    #endregion

    #region Natayark 账户相关功能

    private void ReloadNaidData()
    {
        ModBase.RunInNewThread(() =>
        {
            try
            {
                #region 1. 登录令牌有效期检查

                // 检查 Token 是否过期
                var expireTime = Convert.ToDateTime(States.Link.NaidRefreshExpireTime);
                if (expireTime.CompareTo(DateTime.Now) < 0)
                {
                    States.Link.NaidRefreshToken = "";
                    HintService.Hint(Lang.Text("Tools.GameLink.Natayark.TokenExpired"), HintType.Error);
                    return;
                }

                #endregion

                #region 2. 异步获取数据并同步等待

                // 调用异步方法并阻塞获取结果
                NatayarkProfileManager.GetNaidDataAsync(States.Link.NaidRefreshToken, true).GetAwaiter().GetResult();

                // 等待用户名加载，设置 10 秒超时防止线程卡死
                var retryCount = 0;
                while (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username) && retryCount < 10)
                {
                    Thread.Sleep(1000);
                    retryCount++;
                }

                if (string.IsNullOrWhiteSpace(NatayarkProfileManager.NaidProfile.Username))
                    throw new Exception("Timeout waiting for username");

                #endregion

                #region 3. UI 状态更新

                ModBase.RunInUi(() =>
                {
                    var profile = NatayarkProfileManager.NaidProfile;

                    // 状态 0 为正常
                    if (profile.Status == 0)
                    {
                        RefreshIdentityUi();
                    }
                    else
                    {
                        LabNatayarkUserName.Text = $"{profile.Username} {Lang.Text("Tools.GameLink.Natayark.Abnormal")}";
                        LabNatayarkUserName.Opacity = 0.6;
                    }
                });

                #endregion
            }
            catch (Exception ex)
            {
                #region 错误处理

                ModBase.Log(ex, "Failed to refresh Natayark ID info, re-login required");

                ModBase.RunInUi(() =>
                {
                    States.Link.NaidRefreshToken = string.Empty;
                    RefreshIdentityUi();
                    HintService.Hint(Lang.Text("Tools.GameLink.Natayark.FetchFailed"), HintType.Error);
                });

                #endregion
            }
        }, "Natayark Profile Refresh");
    }

    private void LabNatayarkUserName_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // If Not IsLobbyAvailable Then
        // Hint("大厅功能暂不可用，请稍后再试", HintType.Critical)
        // Exit Sub
        // End If

        if (!IsNatayarkLoggedIn())
        {
            States.Link.NaidRefreshToken = string.Empty;
            // 当前未登录，显示登录选项
            if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Natayark.LoginPrompt"), Lang.Text("Tools.GameLink.Natayark.LoginTitle"), Lang.Text("Tools.GameLink.Natayark.Continue"), Lang.Text("Common.Action.Cancel")) == 1)
            {
                LabNatayarkUserName.Text = Lang.Text("Tools.GameLink.Natayark.BrowserContinue");
                LabNatayarkUserName.Opacity = 0.6d;
                BtnNatayarkUserName.IsEnabled = false;
                ModWebServerCompat.StartNaidAuthorize(success =>
                {
                    ModBase.RunInUi(() => BtnNatayarkUserName.IsEnabled = true);
                    if (success)
                    {
                        HintService.Hint(Lang.Text("Tools.GameLink.Natayark.LoginComplete"), HintType.Success);
                        ReloadNaidData();
                    }
                    else
                    {
                        ModBase.RunInUi(RefreshIdentityUi);
                    }
                });
            }
        }
        // 当前已登录，显示登出选项
        else if (ModMain.MyMsgBox(Lang.Text("Tools.GameLink.Natayark.LogoutConfirm"), Lang.Text("Tools.GameLink.Natayark.LogoutTitle"), Lang.Text("Common.Action.Confirm"), Lang.Text("Common.Action.Cancel")) == 1)
        {
            States.Link.NaidRefreshTokenConfig.Reset();
            States.Link.NaidRefreshToken = "";
            RefreshIdentityUi();
            ModBase.Log("[Link] 已退出登录 Natayark Network");
            HintService.Hint(Lang.Text("Tools.GameLink.Natayark.LogoutComplete"), HintType.Success, false);
        }
    }

    #endregion

    // 网络测试功能
    private async void BtnNetTest_Click(object sender, MouseButtonEventArgs e)
    {
        if (!BtnNatTest.IsEnabled)
            return;

        e.Handled = true;
        ModBase.Log("[Link] 点击网络测试");
        try
        {
            BtnNatTest.IsEnabled = false;
            LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Testing");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            ModBase.Log($"[Link] 网络测试状态已显示：{LabNatType.Text}");
            var status = await CliNetTest.GetNetStatusAsync();
            if (status is null)
                throw new InvalidOperationException("EasyTier 网络测试没有返回有效结果。");
            LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Result",
                CliNetTest.GetNatTypeString(status.UdpNatType),
                CliNetTest.GetNatTypeString(status.TcpNatType));
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Link] 获取网络测试结果失败", ModBase.LogLevel.Hint,
                Lang.Text("Tools.GameLink.Error.NetworkTestFailed"));
            if (LabNatType is not null)
                LabNatType.Text = Lang.Text("Tools.GameLink.Nat.Failed");
        }
        finally
        {
            if (BtnNatTest is not null)
                BtnNatTest.IsEnabled = true;
        }
    }

    private void PasteLobbyId(object sender, MouseButtonEventArgs e)
    {
        string lobbyId;
        try
        {
            lobbyId = Clipboard.GetText(TextDataFormat.Text);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "从剪贴板识别大厅编号出错");
            return;
        }

        if (!string.IsNullOrEmpty(lobbyId))
            TextJoinLobbyId.Text = lobbyId;
        else
            HintService.Hint(Lang.Text("Tools.GameLink.Join.InvalidText"));
    }

    private void ClearLobbyId(object sender, MouseButtonEventArgs e)
    {
        TextJoinLobbyId.Text = string.Empty;
    }

    #endregion

    #region PanSelect | 种类选择页面

    // 刷新按钮
    private async void BtnRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        ModBase.Log("[Link] 开始刷新本地 Minecraft 世界");
        try
        {
            BtnRefresh.IsEnabled = false;
            await RefreshLocalWorldsAsync();
            ModBase.Log($"[Link] 本地世界刷新完成，共发现 {LobbyService.DiscoveredWorlds.Count} 个世界");
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Link] 刷新本地 Minecraft 世界失败", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerMessage", ex.Message), HintType.Error);
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private static async Task RefreshLocalWorldsAsync()
    {
        await LobbyService.DiscoverWorldAsync();
        foreach (var world in global::PclNex.EasyTierLobby.Services.EasyTierLobbyService.FindWorlds())
        {
            if (LobbyService.DiscoveredWorlds.All(existing => existing.Port != world.Port))
                LobbyService.DiscoveredWorlds.Add(new FoundWorld(world.Name, world.Port));
        }
    }

    private async void BtnInputPort_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            BtnInputPort.IsEnabled = false;
            if (!ModLink.LobbyPrecheck()) return;
            var port = await PromptForMinecraftPortAsync();
            if (port is not null)
                await CreateLobbyAsync(port.Value);
        }
        finally
        {
            BtnInputPort.IsEnabled = true;
        }
    }

    private static async Task<int?> PromptForMinecraftPortAsync()
    {
        var input = ModMain.MyMsgBoxInput(Lang.Text("Tools.GameLink.Create.EnterPort"),
            validateRules: [new IntValidator(65535, 1024)]);
        if (!int.TryParse(input, out var port))
            return null;

        using var ping = McPingServiceFactory.CreateService("127.0.0.1", port, 5000);
        var result = await ping.PingAsync();
        if (result is not null && result.Version.Protocol != 0)
            return port;

        HintService.Hint(Lang.Text("Tools.GameLink.Create.NotMcPort"), HintType.Error);
        return null;
    }

    // 创建大厅
    private async void BtnCreate_Click(object sender, MouseButtonEventArgs e)
    {
        if (Interlocked.Exchange(ref _createInProgress, 1) != 0)
            return;

        ModBase.Log("[Link] 点击创建大厅");
        try
        {
            BtnCreate.IsEnabled = false;

            if (!ModLink.LobbyPrecheck())
                return;

            int? port;
            if (ComboWorldList.SelectedItem is MyComboBoxItem selectedWorld)
            {
                port = (int)selectedWorld.Tag;
            }
            else
            {
                ModBase.Log("[Link] 未自动发现 Minecraft 世界，转为手动输入端口");
                port = await PromptForMinecraftPortAsync();
            }

            if (port is not null)
                await CreateLobbyAsync(port.Value);
        }
        finally
        {
            BtnCreate.IsEnabled = true;
            Interlocked.Exchange(ref _createInProgress, 0);
        }
    }

    private async Task CreateLobbyAsync(int port)
    {
        ModBase.Log("[Link] 创建大厅，端口：" + port);


        var username = LobbyInfoProvider.GetUsername();

        ModBase.RunInUi(() =>
        {
            BtnFinishPing.Visibility = Visibility.Collapsed;
            LabFinishPing.Text = "-ms";
            BtnConnectType.Visibility = Visibility.Collapsed;
            LabConnectType.Text = Lang.Text("Tools.GameLink.Finish.Connecting");
            CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
            StackPlayerList.Children.Clear();
            LabConnectUserName.Text = username;
            LabConnectUserType.Text = Lang.Text("Tools.GameLink.Finish.Host");
            LabFinishId.Text = string.Empty;
            BtnFinishCopyIp.Visibility = Visibility.Collapsed;
            BtnCreate.IsEnabled = true;
            BtnFinishExit.Text = Lang.Text("Tools.GameLink.Finish.CloseLobby");
        });

        try
        {
            var res = await LobbyService.CreateLobbyAsync(port, username).ConfigureAwait(true);

            if (res && !string.IsNullOrWhiteSpace(LobbyService.CurrentLobbyCode))
                ModBase.RunInUi(() =>
                {
                    LabFinishId.Text = LobbyService.CurrentLobbyCode;
                    CurrentSubpage = Subpages.PanFinish;
                });
            else
                ModBase.RunInUi(() =>
                {
                    CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                    StackPlayerList.Children.Clear();
                    BtnCreate.IsEnabled = true;
                    CurrentSubpage = Subpages.PanSelect;
                });
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Link] 创建大厅失败", ModBase.LogLevel.Hint);
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerMessage", ex.Message), HintType.Error);
            ModBase.RunInUi(() =>
            {
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                BtnCreate.IsEnabled = true;
                CurrentSubpage = Subpages.PanSelect;
            });
        }
    }

    // 加入大厅
    private async void BtnJoin_Click(object sender, MouseButtonEventArgs e)
    {
        if (Interlocked.Exchange(ref _joinInProgress, 1) != 0)
            return;

        ModBase.Log("[Link] 点击加入大厅");
        try
        {
            if (!ModLink.LobbyPrecheck())
                return;

            var id = TextJoinLobbyId.Text.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                HintService.Hint(Lang.Text("Tools.GameLink.Join.InvalidText"), HintType.Error);
                return;
            }

            ModBase.Log($"[Link] 开始加入大厅：{id}");
            var username = LobbyInfoProvider.GetUsername();

            ModBase.RunInUi(() =>
            {
                BtnFinishPing.Visibility = Visibility.Visible;
                LabFinishPing.Text = "-ms";
                BtnConnectType.Visibility = Visibility.Visible;
                LabConnectType.Text = Lang.Text("Tools.GameLink.Finish.Connecting");
                CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                StackPlayerList.Children.Clear();
                LabConnectUserName.Text = username;
                LabConnectUserType.Text = Lang.Text("Tools.GameLink.Finish.Guest");
                LabFinishId.Text = id;
                BtnFinishCopyIp.Visibility = Visibility.Visible;
                CurrentSubpage = Subpages.PanFinish;
            });

            try
            {
                var res = await LobbyService.JoinLobbyAsync(id, username).ConfigureAwait(true);

                if (!res)
                    ModBase.RunInUi(() =>
                    {
                        CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                        StackPlayerList.Children.Clear();
                        CurrentSubpage = Subpages.PanSelect;
                    });
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, "[Link] 加入大厅失败", ModBase.LogLevel.Hint);
                HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerMessage", ex.Message), HintType.Error);
                ModBase.RunInUi(() =>
                {
                    CardPlayerList.Title = Lang.Text("Tools.GameLink.Member.ListLoading");
                    StackPlayerList.Children.Clear();
                    CurrentSubpage = Subpages.PanSelect;
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _joinInProgress, 0);
        }
    }

    private void TextJoinLobbyId_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnJoin_Click(sender, null);
    }

    #endregion

    #region PanLoad | 加载中页面

    // 承接状态切换的 UI 改变
    private void OnLoadStateChanged(ModLoader.LoaderBase loader, ModBase.LoadState newState, ModBase.LoadState oldState)
    {
    }

    private static string _loadStep = "准备初始化";

    private static void SetLoadDesc(string intro, string step)
    {
        ModBase.Log("连接步骤：" + intro);
        _loadStep = step;
        ModBase.RunInUiWait(() =>
        {
            if (_currentPage is null || !_currentPage.LabLoadDesc.IsLoaded)
                return;
            _currentPage.LabLoadDesc.Text = intro;
            _currentPage.UpdateProgress();
        });
    }

    // 承接重试
    private void CardLoad_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (initLoader.State != ModBase.LoadState.Failed)
            return;
        initLoader.Start(isForceRestart: true);
    }

    // 取消加载
    private void CancelLoad(object sender, EventArgs eventArgs)
    {
        if (initLoader.State == ModBase.LoadState.Loading)
        {
            CurrentSubpage = Subpages.PanSelect;
            initLoader.Abort();
        }
        else
        {
            initLoader.State = ModBase.LoadState.Waiting;
        }
    }

    // 进度改变
    private void UpdateProgress(double value = -1)
    {
        if (value == -1)
            value = initLoader.Progress;
        var displayingProgress = ColumnProgressA.Width.Value;
        if (Math.Round(value - displayingProgress, 3) == 0d)
            return;
        if (displayingProgress > value)
        {
            ColumnProgressA.Width = new GridLength(value, GridUnitType.Star);
            ColumnProgressB.Width = new GridLength(1d - value, GridUnitType.Star);
            ModAnimation.AniStop("LobbyController Progress");
        }
        else
        {
            var newProgress = value == 1d ? 1d : (value - displayingProgress) * 0.2d + displayingProgress;
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaGridLengthWidth(ColumnProgressA, newProgress - ColumnProgressA.Width.Value, 300,
                        ease: new ModAnimation.AniEaseOutFluent()),
                    ModAnimation.AaGridLengthWidth(ColumnProgressB, 1d - newProgress - ColumnProgressB.Width.Value, 300,
                        ease: new ModAnimation.AniEaseOutFluent())
                }, "LobbyController Progress");
        }
    }

    private void CardResized(object sender, SizeChangedEventArgs sizeChangedEventArgs)
    {
        RectProgressClip.Rect = new Rect(0d, 0d, CardLoad.ActualWidth, 12d);
    }

    #endregion

    #region PanFinish | 加载完成页面

    // 退出
    private async void BtnFinishExit_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        if (ModMain.MyMsgBox(
                Lang.Text(LobbyService.IsHost
                    ? "Tools.GameLink.Exit.ConfirmMessageWithHost"
                    : "Tools.GameLink.Exit.ConfirmMessage"),
                Lang.Text("Tools.GameLink.Exit.ConfirmTitle"),
                Lang.Text("Common.Action.Confirm"),
                Lang.Text("Common.Action.Cancel"),
                isWarn: true
            ) == 1)
        {
            CurrentSubpage = Subpages.PanSelect;
            BtnFinishExit.Text = Lang.Text("Tools.GameLink.Finish.Exit");
            await LobbyService.LeaveLobbyAsync().ConfigureAwait(true);
            await RefreshLocalWorldsAsync();
        }
    }

    // 复制大厅编号
    private void BtnFinishCopy_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        ModBase.ClipboardSet(LabFinishId.Text);
    }

    // 复制 IP
    private void BtnFinishCopyIp_Click(object sender, ModBase.RouteEventArgs routeEventArgs)
    {
        var forward = LobbyInfoProvider.McForward;
        if (LobbyService.CurrentState != LobbyState.Connected || forward is null || forward.LocalPort <= 0)
        {
            HintService.Hint(Lang.Text("Tools.GameLink.Error.ServerExit"), HintType.Error);
            CurrentSubpage = Subpages.PanSelect;
            return;
        }

        var ip = $"127.0.0.1:{forward.LocalPort}";
        ModMain.MyMsgBox(Lang.Text("Tools.GameLink.CopyIp.Message", ip),
            Lang.Text("Tools.GameLink.CopyIp.Title"),
            Lang.Text("Common.Action.Copy"),
            Lang.Text("Tools.GameLink.CopyIp.Back"),
            button1Action: () => ModBase.ClipboardSet(ip));
    }

    #endregion

    #region 子页面管理

    public enum Subpages
    {
        PanEula,
        PanSelect,
        PanFinish
    }

    public Subpages CurrentSubpage
    {
        get => field;
        set
        {
            if (field == value)
                return;
            field = value;
            ModBase.Log("[Link] 子页面更改为 " + ModBase.GetStringFromEnum(value));
            PageOnContentExit();
        }
    } = States.Link.LinkEula ? Subpages.PanSelect : Subpages.PanEula;

    private void PageLinkLobby_OnPageEnter()
    {
        PanEula.Visibility =
            CurrentSubpage == Subpages.PanEula ? Visibility.Visible : Visibility.Collapsed;
        PanSelect.Visibility =
            CurrentSubpage == Subpages.PanSelect ? Visibility.Visible : Visibility.Collapsed;
        PanFinish.Visibility =
            CurrentSubpage == Subpages.PanFinish ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion
}
