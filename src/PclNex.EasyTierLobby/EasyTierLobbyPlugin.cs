using System.Windows;
using System.Windows.Controls;
using System.Runtime.CompilerServices;
using PCL;
using PCL.Core.App.Localization;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Mixin;
using PclNex.EasyTierLobby.UI;

namespace PclNex.EasyTierLobby.Mixins;

[Mixin("PCL.PageToolsLeft", Priority = 1100)]
internal static class PageToolsLeftMixin
{
    private static readonly ConditionalWeakTable<PageToolsLeft, ToolsPageState> States = new();

    [Inject(".ctor", At = MixinAt.Return)]
    private static void AttachLobbyEntry([This] PageToolsLeft page)
    {
        PluginRuntime.Initialize();
        PluginNavigation.AttachTools(page);
        States.GetOrCreateValue(page).RestoreDefaultOnFirstLoad = true;
    }

    [Inject("PageLinkLeft_Loaded", At = MixinAt.Return)]
    private static void RestoreCeDefaultPage([This] PageToolsLeft page)
    {
        var state = States.GetOrCreateValue(page);
        if (!state.RestoreDefaultOnFirstLoad)
            return;

        state.RestoreDefaultOnFirstLoad = false;
        PluginNavigation.SelectToolsEntry(page);
    }

    [Inject("PageGet", At = MixinAt.Head, Cancellable = true)]
    private static void ResolveLobbyPage(
        [This] PageToolsLeft page,
        [Arg(0)] FormMain.PageSubType? id,
        CallbackInfo<object> callback)
    {
        if ((id ?? page.pageID) == PluginNavigation.ToolsGameLink)
            callback.SetReturnValue(PluginRuntime.ToolsPage);
    }

    private sealed class ToolsPageState
    {
        public bool RestoreDefaultOnFirstLoad { get; set; }
    }
}

[Mixin("PCL.PageSetupLeft", Priority = 1100)]
internal static class PageSetupLeftMixin
{
    [Inject(".ctor", At = MixinAt.Return)]
    private static void AttachSettingsEntry([This] PageSetupLeft page)
    {
        PluginRuntime.Initialize();
        PluginNavigation.AttachSettings(page);
    }

    [Inject("PageGet", At = MixinAt.Head, Cancellable = true)]
    private static void ResolveSettingsPage(
        [This] PageSetupLeft page,
        [Arg(0)] FormMain.PageSubType? id,
        CallbackInfo<object> callback)
    {
        if ((id ?? page.pageID) == PluginNavigation.SetupGameLink)
            callback.SetReturnValue(PluginRuntime.SettingsPage);
    }
}

[Mixin("PCL.PageToolsTest", Priority = 1100)]
internal static class PageToolsTestMixin
{
    [Inject(".ctor", At = MixinAt.Return)]
    private static void AttachServerQuery([This] PageToolsTest page)
    {
        PluginRuntime.Initialize();
        PluginNavigation.AttachServerQuery(page);
    }
}

internal static class PluginRuntime
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static PageToolsGameLink? _toolsPage;
    private static PageSetupGameLink? _settingsPage;

    public static PageToolsGameLink ToolsPage => _toolsPage ??= new PageToolsGameLink();
    public static PageSetupGameLink SettingsPage => _settingsPage ??= new PageSetupGameLink();

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized) return;

            var dataDirectory = System.IO.Path.Combine(PCL.Core.App.Paths.Plugins, "data", "pclnex.easytier");
            PCL.CeUiRuntimeBridge.Initialize(dataDirectory);
            PCL.CeResourceOverride.Apply();
            EasyTierEntity.CleanupOrphanedLobbyProcesses();
            System.Windows.Application.Current.Exit += (_, _) => Shutdown();
            _initialized = true;
        }
    }

    private static void Shutdown()
    {
        try
        {
            LobbyService.LeaveLobbyAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ModBase.Log(exception, "[Link] 退出时清理 EasyTier 大厅失败");
        }
    }
}

internal static class PluginNavigation
{
    public const FormMain.PageSubType ToolsGameLink = (FormMain.PageSubType)1;
    public const FormMain.PageSubType SetupGameLink = (FormMain.PageSubType)7;

    private const string ToolsCategoryName = "TextGameLinkCategory";
    private const string ToolsItemName = "ItemGameLink";
    private const string SettingsCategoryName = "TextToolsCategory";
    private const string SettingsItemName = "ItemGameLink";
    private const string ServerQueryCardName = "CardServerQuery";

    public static void AttachTools(PageToolsLeft page)
    {
        if (page.FindName(ToolsItemName) is MyListItem) return;
        var panel = page.FindName("PanItem") as StackPanel
                    ?? throw new InvalidOperationException("PageToolsLeft.PanItem 不存在。");

        var category = new TextBlock
        {
            Name = ToolsCategoryName,
            Margin = new Thickness(13, 5, 5, 3),
            Opacity = 0.6,
            FontSize = 12
        };
        category.SetResourceReference(TextBlock.TextProperty, "Tools.Left.Multiplayer");
        var refresh = new MyIconButton
        {
            Tag = "1",
            LogoScale = 0.85,
            SvgIcon = "lucide/refresh-cw"
        };
        refresh.SetResourceReference(ToolTipService.ToolTipProperty, "Common.Action.Refresh");
        ToolTipService.SetPlacement(refresh, System.Windows.Controls.Primitives.PlacementMode.Right);
        ToolTipService.SetInitialShowDelay(refresh, 200);
        ToolTipService.SetVerticalOffset(refresh, -1);

        var item = new MyListItem
        {
            Name = ToolsItemName,
            IsScaleAnimationEnabled = false,
            Type = MyListItem.CheckType.RadioBox,
            Tag = "1",
            MinPaddingRight = 35,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Top,
            LogoScale = 0.85,
            SvgIcon = "lucide/link-2",
            Buttons = [refresh]
        };
        item.SetResourceReference(MyListItem.TitleProperty, "Tools.Left.Lobby");

        item.Check += (_, _) => page.PageChange(ToolsGameLink);
        refresh.Click += (_, _) =>
        {
            PluginRuntime.ToolsPage.Reload();
            item.Checked = true;
        };

        panel.Children.Insert(0, category);
        panel.Children.Insert(1, item);
        page.RegisterName(ToolsCategoryName, category);
        page.RegisterName(ToolsItemName, item);
        page.pageID = ToolsGameLink;
        item.SetChecked(true, false, false);
    }

    public static void SelectToolsEntry(PageToolsLeft page)
    {
        if (page.FindName(ToolsItemName) is not MyListItem item)
            return;

        // PageToolsLeft selects ToolsTest during Loaded. Force a real page transition
        // before restoring the injected radio item so the right page cannot remain stale.
        page.pageID = FormMain.PageSubType.ToolsTest;
        page.PageChange(ToolsGameLink);
        item.SetChecked(true, false, false);
    }

    public static void AttachServerQuery(PageToolsTest page)
    {
        if (page.FindName(ServerQueryCardName) is MyCard)
            return;

        var panel = page.FindName("PanMain") as StackPanel
                    ?? throw new InvalidOperationException("PageToolsTest.PanMain 不存在。");
        var skinCard = page.FindName("CardSkin") as UIElement
                       ?? throw new InvalidOperationException("PageToolsTest.CardSkin 不存在。");
        var insertIndex = panel.Children.IndexOf(skinCard) + 1;

        var card = new MyCard
        {
            Name = ServerQueryCardName,
            Margin = new Thickness(0, 0, 0, 15)
        };
        card.SetResourceReference(MyCard.TitleProperty, "Tools.Test.ServerQuery.Title");
        card.Children.Add(new MinecraftServerQuery
        {
            Margin = new Thickness(15, 40, 15, 15)
        });

        panel.Children.Insert(insertIndex, card);
        page.RegisterName(ServerQueryCardName, card);
    }

    public static void AttachSettings(PageSetupLeft page)
    {
        if (page.FindName(SettingsItemName) is MyListItem) return;
        var panel = page.FindName("PanItem") as StackPanel
                    ?? throw new InvalidOperationException("PageSetupLeft.PanItem 不存在。");
        var launcherCategory = page.FindName("TextLauncherCategory") as UIElement
                               ?? throw new InvalidOperationException("PageSetupLeft.TextLauncherCategory 不存在。");
        var insertIndex = panel.Children.IndexOf(launcherCategory);

        var category = new TextBlock
        {
            Name = SettingsCategoryName,
            Margin = new Thickness(13, 5, 5, 3),
            Opacity = 0.6,
            FontSize = 12
        };
        category.SetResourceReference(TextBlock.TextProperty, "Setup.Left.Category.Tools");
        var reset = new MyIconButton
        {
            Tag = "7",
            LogoScale = 0.9,
            SvgIcon = "lucide/rotate-ccw"
        };
        reset.SetResourceReference(ToolTipService.ToolTipProperty, "Common.Action.Initialize");
        ToolTipService.SetPlacement(reset, System.Windows.Controls.Primitives.PlacementMode.Right);
        ToolTipService.SetInitialShowDelay(reset, 200);
        ToolTipService.SetVerticalOffset(reset, -1);

        var item = new MyListItem
        {
            Name = SettingsItemName,
            IsScaleAnimationEnabled = false,
            Type = MyListItem.CheckType.RadioBox,
            Tag = "7",
            MinPaddingRight = 35,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Top,
            SvgIcon = "lucide/bubbles",
            Buttons = [reset]
        };
        item.SetResourceReference(MyListItem.TitleProperty, "Setup.Left.Item.GameLink");

        item.Check += (_, _) => page.PageChange(SetupGameLink);
        reset.Click += (_, _) =>
        {
            if (ModMain.MyMsgBox(
                    Lang.Text("Setup.Left.Reset.GameLink.Message"),
                    Lang.Text("Setup.Left.Reset.Title"),
                    button2: Lang.Text("Common.Action.Cancel"),
                    isWarn: true) != 1)
                return;

            PluginRuntime.SettingsPage.Reset();
            item.Checked = true;
        };

        panel.Children.Insert(insertIndex, category);
        panel.Children.Insert(insertIndex + 1, item);
        page.RegisterName(SettingsCategoryName, category);
        page.RegisterName(SettingsItemName, item);
    }
}
