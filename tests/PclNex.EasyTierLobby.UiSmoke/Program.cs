using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PCL;
using PCL.Core.App.Configuration;
using PCL.Core.App.IoC;
using PclNex.EasyTierLobby.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Environment.SetEnvironmentVariable(
            "windir",
            Environment.GetEnvironmentVariable("windir")
                ?? Environment.GetEnvironmentVariable("SystemRoot")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        typeof(System.Windows.Application)
            .GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, typeof(PCL.Application).Assembly);
        var application = System.Windows.Application.Current as PCL.Application ?? new PCL.Application();
        application.InitializeComponent();
        var configService = (ILifecycleService)Activator.CreateInstance(typeof(ConfigService), nonPublic: true)!;
        configService.StartAsync().GetAwaiter().GetResult();
        Assert(ConfigService.IsInitialized, "Nex ConfigService did not initialize for the UI smoke test.");

        var dataDirectory = Path.Combine(Path.GetTempPath(), $"pclnex-easytier-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        try
        {
            CeUiRuntimeBridge.Initialize(dataDirectory);
            CeResourceOverride.Apply();

            var lobbyPage = new PageToolsGameLink();
            var settingsPage = new PageSetupGameLink();
            Assert(lobbyPage.FindName("PanLoad") is Grid, "Copied CE lobby page is missing PanLoad.");
            Assert(lobbyPage.FindName("PanSelect") is FrameworkElement, "Copied CE lobby page is missing PanSelect.");
            Assert(lobbyPage.FindName("PanFinish") is FrameworkElement, "Copied CE lobby page is missing PanFinish.");
            Assert(lobbyPage.FindName("BtnCreate") is MyButton, "Copied CE lobby page is missing BtnCreate.");
            Assert(settingsPage.FindName("TextLinkUsername") is MyTextBox, "Copied CE settings page is missing TextLinkUsername.");

            var toolsLeft = new PageToolsLeft();
            var setupLeft = new PageSetupLeft();
            var toolsTest = new PageToolsTest();
            InvokeNavigation("AttachTools", toolsLeft);
            InvokeNavigation("AttachSettings", setupLeft);
            InvokeNavigation("AttachServerQuery", toolsTest);

            var toolsPanel = (StackPanel)toolsLeft.FindName("PanItem");
            Assert(toolsPanel.Children[0] is TextBlock { Text: "联机" }, "CE multiplayer category is not first in Tools.");
            Assert(toolsPanel.Children[1] is MyListItem { Title: "大厅", SvgIcon: "lucide/link-2" }, "CE lobby item is not in its original Tools position.");
            Assert(ReferenceEquals(toolsPanel.Children[2], toolsLeft.FindName("TextToolsCategory")), "Nex utilities category moved unexpectedly.");

            var setupPanel = (StackPanel)setupLeft.FindName("PanItem");
            var launcherCategory = (UIElement)setupLeft.FindName("TextLauncherCategory");
            var launcherIndex = setupPanel.Children.IndexOf(launcherCategory);
            Assert(setupPanel.Children[launcherIndex - 2] is TextBlock { Text: "工具" }, "CE tools category is not before Launcher settings.");
            Assert(setupPanel.Children[launcherIndex - 1] is MyListItem { Title: "联机", SvgIcon: "lucide/bubbles" }, "CE link settings item is not in its original position.");

            var toolsTestPanel = (StackPanel)toolsTest.FindName("PanMain");
            var skinCard = (UIElement)toolsTest.FindName("CardSkin");
            var skinIndex = toolsTestPanel.Children.IndexOf(skinCard);
            var serverCard = toolsTestPanel.Children[skinIndex + 1] as MyCard;
            Assert(serverCard is { Title: "瞅眼服务器" }, "CE server query card is not after CardSkin.");
            Assert(serverCard!.Children.OfType<MinecraftServerQuery>().SingleOrDefault() is not null,
                "CE MinecraftServerQuery control is missing from the injected card.");
        }
        finally
        {
            application.Shutdown();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }

        Console.WriteLine("CE_PAGE_CONSTRUCTION=OK");
        Console.WriteLine("CE_LEFT_POSITION_INJECTION=OK");
        Console.WriteLine("CE_SERVER_QUERY_INJECTION=OK");
    }

    private static void InvokeNavigation(string methodName, object page)
    {
        var pluginAssembly = typeof(PageToolsGameLink).Assembly;
        var navigationType = pluginAssembly.GetType("PclNex.EasyTierLobby.Mixins.PluginNavigation")
                             ?? throw new InvalidOperationException("PluginNavigation is missing.");
        var method = navigationType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
                     ?? throw new InvalidOperationException($"PluginNavigation.{methodName} is missing.");
        method.Invoke(null, [page]);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
