using System.Reflection;
using System.Text.Json;
using PclNex.EasyTierLobby.UI;

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var toolsCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeUi", "PageTools", "PageToolsGameLink.xaml.cs"));
var toolsXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeUi", "PageTools", "PageToolsGameLink.xaml"));
var settingsCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeUi", "PageSetup", "PageSetupGameLink.xaml.cs"));
var settingsXaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeUi", "PageSetup", "PageSetupGameLink.xaml"));
var lobbyControllerCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Lobby", "LobbyController.cs"));
var scaffoldingFactoryCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Scaffolding", "ScaffoldingFactory.cs"));
var scaffoldingContextCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Scaffolding", "Server", "ScaffoldingServerContext.cs"));
var scaffoldingServerCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Scaffolding", "Server", "ScaffoldingServer.cs"));
var playerListHandlerCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Scaffolding", "Server", "Handlers", "GetPlayerProfileListHandler.cs"));
var pluginCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "EasyTierLobbyPlugin.cs"));
var lobbyServiceCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Lobby", "LobbyService.cs"));
var easyTierEntityCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "PclNex.EasyTierLobby", "CeLink", "Scaffolding", "EasyTier", "EasyTierEntity.cs"));

var bindings = new Dictionary<string, string>
{
    ["BtnJoin"] = "BtnJoin_Click",
    ["BtnPaste"] = "PasteLobbyId",
    ["BtnClearLobbyId"] = "ClearLobbyId",
    ["BtnCreate"] = "BtnCreate_Click",
    ["BtnRefresh"] = "BtnRefresh_Click",
    ["BtnInputPort"] = "BtnInputPort_Click",
    ["BtnEulaAgree"] = "BtnAgreeEula_Click",
    ["BtnEulaStop"] = "BtnEulaStop_Click",
    ["BtnLoadCancel"] = "CancelLoad",
    ["BtnFinishCopyIp"] = "BtnFinishCopyIp_Click",
    ["BtnFinishCopy"] = "BtnFinishCopy_Click",
    ["BtnFinishExit"] = "BtnFinishExit_Click"
};

AssertConstructorCallsBinding(typeof(PageToolsGameLink));
foreach (var (control, handler) in bindings)
{
    AssertExactlyOnce(toolsCode, $"{control}.Click += {handler};");
    AssertAbsent(toolsXaml, $"Click=\"{handler}\"");
    Console.WriteLine($"{control}={handler}:OK");
}

AssertAbsent(toolsXaml, "MouseLeftButtonUp=\"BtnNetTest_Click\"");
AssertExactlyOnce(toolsCode, "BtnNatTest.AddHandler(PreviewMouseLeftButtonUpEvent,");
AssertExactlyOnce(toolsCode, "new MouseButtonEventHandler(BtnNetTest_Click), handledEventsToo: true");
AssertExactlyOnce(toolsCode, "LabNatType.Text = Lang.Text(\"Tools.GameLink.Nat.Testing\");");
AssertExactlyOnce(toolsCode, "DispatcherPriority.Render");
AssertAbsent(settingsCode, "BtnNetTest_Click");
AssertAbsent(settingsXaml, "NetworkTest");
Console.WriteLine("BtnNatTest=BtnNetTest_Click:OK");

AssertExactlyOnce(settingsXaml, "x:Name=\"TextLinkUsername\"");
AssertAbsent(settingsXaml, "TextChanged=\"TextBoxChange\"");
AssertExactlyOnce(settingsCode, "TextLinkUsername.TextChanged += TextBoxChange;");
AssertExactlyOnce(settingsCode, "TextLinkUsername.Text = Config.Link.Username;");
AssertExactlyOnce(settingsCode, "if (_isReloadingUsername)");
AssertExactlyOnce(settingsCode, "SetByTag(sender.Tag?.ToString(), sender.Text);");
AssertAbsent(toolsXaml, "TextLinkUsername");
AssertAbsent(toolsXaml, "TextLocalUsername");
AssertExactlyOnce(toolsCode, "Config.Link.UsernameChanged += OnUsernameChanged;");
AssertExactlyCount(toolsCode, "LobbyInfoProvider.GetUsername();", 2);
AssertExactlyOnce(toolsCode, "private static bool IsNatayarkLoggedIn()");
AssertExactlyOnce(toolsCode, "private void RefreshIdentityUi()");
AssertAbsent(toolsCode, "Config.Link.Username = string.Empty;");
Console.WriteLine("Username=SettingsRealtimeToLobby:OK");

AssertExactlyOnce(toolsXaml, "x:Name=\"HintAnnounce\"");
AssertExactlyCount(toolsCode, "HintAnnounce.Visibility = Visibility.Visible;", 3);
AssertExactlyOnce(toolsCode, "maxVer < 999 && ModBase.versionCode > maxVer");
AssertExactlyOnce(scaffoldingFactoryCode, "PCL.Nex {Basics.VersionName}");
AssertExactlyOnce(scaffoldingContextCode, "PCL.Nex {Basics.VersionName}");
AssertExactlyOnce(playerListHandlerCode, "PCL.Nex {Basics.VersionName}");
Console.WriteLine("BrandAndAnnouncement=PCL.Nex:OK");

AssertExactlyOnce(lobbyControllerCode, "ScaffoldingFactory.CreateServer(port, username)");
AssertExactlyOnce(scaffoldingFactoryCode, "public static ScaffoldingServerEntity CreateServer(int mcPort, string playerName)");
AssertExactlyOnce(scaffoldingFactoryCode, "CheckEasyTierStatusAsync(ct)");
AssertAbsent(scaffoldingFactoryCode, "CreateServerAsync");
AssertAbsent(scaffoldingFactoryCode, "readinessCts");
AssertExactlyOnce(scaffoldingServerCode, "new TcpListener(IPAddress.Any, port)");
AssertAbsent(scaffoldingServerCode, "new TcpListener(IPAddress.Loopback, port)");
Console.WriteLine("HostCreation=CeNonBlocking:OK");

AssertExactlyOnce(pluginCode, "Factory = () => _toolsPage ??= new PageToolsGameLink()");
AssertExactlyOnce(toolsCode, "private void RestoreLobbyUiFromState()");
AssertExactlyOnce(toolsCode, "ModBase.Log(\"[Link] 已从联机服务恢复已连接大厅页面\");");
AssertExactlyCount(lobbyServiceCode, "CurrentState != LobbyState.Initialized", 2);
AssertAbsent(easyTierEntityCode, ".AddFlag(\"p2p-only\")");
AssertExactlyOnce(easyTierEntityCode, "Config.Link.RelayType == LinkRelayBehavior.ForceRelay, \"disable-p2p\"");
AssertExactlyOnce(pluginCode, "EasyTierEntity.CleanupOrphanedLobbyProcesses();");
AssertExactlyOnce(lobbyServiceCode, "public static ConnectionWay CurrentConnectionWay =>");
AssertExactlyOnce(toolsCode, "LobbyTextHandler.GetConnectTypeName(LobbyService.CurrentConnectionWay)");
AssertAbsent(toolsCode, "ETInfoProvider.GetPlayerList");
Console.WriteLine("LobbyLifecycle=SingleActiveRelayCapable:OK");

AssertExactlyCount(toolsCode, "await RefreshLocalWorldsAsync();", 4);
AssertExactlyOnce(toolsCode, "private static async Task RefreshLocalWorldsAsync()");
AssertExactlyOnce(lobbyServiceCode, "private static readonly SemaphoreSlim _discoveringGate = new(1, 1);");
AssertExactlyCount(lobbyServiceCode, "CurrentState is not LobbyState.Initialized && CurrentState is not LobbyState.Idle", 2);
AssertExactlyOnce(lobbyServiceCode, "generation = Interlocked.Increment(ref _discoveringGeneration);");
AssertExactlyCount(lobbyServiceCode, "generation == Volatile.Read(ref _discoveringGeneration)", 2);
AssertExactlyOnce(lobbyServiceCode, "DiscoveredWorlds.All(world => world.Port != info.Address.Port)");
AssertExactlyOnce(lobbyServiceCode, "await discoveryTask.ConfigureAwait(false);");
Console.WriteLine("WorldDiscovery=RestoredAfterLeave:OK");

var persistenceDirectory = Path.Combine(Path.GetTempPath(), $"pclnex-lobby-ui-smoke-{Guid.NewGuid():N}");
try
{
    var pluginAssembly = typeof(PageToolsGameLink).Assembly;
    var configType = pluginAssembly.GetType("PCL.Core.App.Config")
        ?? throw new InvalidOperationException("Plugin Config type missing.");
    var linkConfig = configType.GetProperty("Link", BindingFlags.Static | BindingFlags.Public)?.GetValue(null)
        ?? throw new InvalidOperationException("Plugin Config.Link missing.");
    var linkConfigType = linkConfig.GetType();
    var usernameProperty = linkConfigType.GetProperty("Username")
        ?? throw new InvalidOperationException("Plugin Config.Link.Username missing.");
    var usernameChangedEvent = linkConfigType.GetEvent("UsernameChanged")
        ?? throw new InvalidOperationException("Plugin Config.Link.UsernameChanged missing.");
    var persistenceType = pluginAssembly.GetType("PCL.Core.App.LinkPersistence")
        ?? throw new InvalidOperationException("LinkPersistence type missing.");
    var initializeMethod = persistenceType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("LinkPersistence.Initialize missing.");
    initializeMethod.Invoke(null, [persistenceDirectory]);

    string? observedUsername = null;
    void OnUsernameChanged(string username) => observedUsername = username;
    Action<string> usernameChangedHandler = OnUsernameChanged;
    usernameChangedEvent.AddEventHandler(linkConfig, usernameChangedHandler);
    try
    {
        const string expectedUsername = "RealtimeSmokeUser";
        usernameProperty.SetValue(linkConfig, expectedUsername);
        if (observedUsername != expectedUsername)
            throw new InvalidOperationException("UsernameChanged did not publish the new username immediately.");

        using var configJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(persistenceDirectory, "link-config.json")));
        if (configJson.RootElement.GetProperty("Username").GetString() != expectedUsername)
            throw new InvalidOperationException("Username was not persisted immediately.");

        var lobbyInfoProviderType = pluginAssembly.GetType("PCL.Core.Link.Lobby.LobbyInfoProvider")
            ?? throw new InvalidOperationException("LobbyInfoProvider type missing.");
        lobbyInfoProviderType.GetProperty("AllowCustomName")?.SetValue(null, true);
        var lobbyUsername = lobbyInfoProviderType.GetMethod("GetUsername")?.Invoke(null, null)?.ToString();
        if (lobbyUsername != expectedUsername)
            throw new InvalidOperationException("Lobby did not read the updated username immediately.");
    }
    finally
    {
        usernameChangedEvent.RemoveEventHandler(linkConfig, usernameChangedHandler);
    }
}
finally
{
    if (Directory.Exists(persistenceDirectory))
        Directory.Delete(persistenceDirectory, recursive: true);
}
Console.WriteLine("Username=RealtimeBehavior:OK");
Console.WriteLine("UI_CLICK_BINDINGS=OK");

static void AssertConstructorCallsBinding(Type pageType)
{
    var constructor = pageType.GetConstructor(Type.EmptyTypes)
        ?? throw new InvalidOperationException($"Default constructor missing: {pageType.FullName}");
    var bindMethod = pageType.GetMethod("BindActionHandlers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("BindActionHandlers method missing from tools page.");
    var il = constructor.GetMethodBody()?.GetILAsByteArray() ?? [];
    if (!ContainsMetadataToken(il, bindMethod.MetadataToken))
        throw new InvalidOperationException("PageToolsGameLink constructor does not call BindActionHandlers.");
}

static bool ContainsMetadataToken(byte[] il, int token)
{
    var bytes = BitConverter.GetBytes(token);
    return il.AsSpan().IndexOf(bytes) >= 0;
}

static void AssertExactlyOnce(string content, string value)
{
    AssertExactlyCount(content, value, 1);
}

static void AssertExactlyCount(string content, string value, int expectedCount)
{
    var count = content.Split(value, StringSplitOptions.None).Length - 1;
    if (count != expectedCount)
        throw new InvalidOperationException($"Expected exactly {expectedCount} occurrence(s) of '{value}', found {count}.");
}

static void AssertAbsent(string content, string value)
{
    if (content.Contains(value, StringComparison.Ordinal))
        throw new InvalidOperationException($"Duplicate XAML binding remains: {value}");
}