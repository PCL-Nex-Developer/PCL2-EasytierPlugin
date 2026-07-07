using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.EasyTierPlugin.Scaffolding;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

[Plugin(
    "pclnex.easytier",
    "EasyTier 联机大厅",
    ThisAssembly.PluginVersion,
    Author = "Nex(XueLing)",
    Description = "实现 EasyTier 联机大厅功能。",
    MinApiVersion = "1.1.0.0",
    Capabilities = PluginCapabilities.RegisterExtension | PluginCapabilities.ContributeTools | PluginCapabilities.ContributeSettings,
    LoadTiming = PluginLoadTiming.WindowCreated)]
public sealed class EasyTierLobbyPlugin : PclPluginBase
{
    private IDisposable? _providerRegistration;
    private IDisposable? _serviceRegistration;
    private IDisposable? _toolsRegistration;
    private IDisposable? _settingsRegistration;
    private EasyTierLobbyTunnelProvider? _provider;

    public override Task LoadAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        base.LoadAsync(context, cancellationToken);

        EasyTierPluginSettings.Initialize(context.Host.Config);

        var extensions = context.Host.Extensions ?? throw new InvalidOperationException("Plugin extension API is unavailable.");
        _provider = new EasyTierLobbyTunnelProvider(context);
        ScaffoldingFactory.SetProvider(_provider);
        _providerRegistration = extensions.RegisterLobbyTunnelProvider(_provider);
        _serviceRegistration = extensions.RegisterLobbyService(new EasyTierLobbyServiceAdapter(), displayName: "EasyTier Lobby Service");

        var ui = context.Host.Ui ?? throw new InvalidOperationException("UI API is unavailable.");
        var pages = context.Host.GetOptionalService("pcl:host:tools-pages") as IPluginPageHostService
            ?? throw new InvalidOperationException("Host tools page service is unavailable.");

        _toolsRegistration = ui.ContributeToolsPanel(new ToolsPanelDescriptor
        {
            Id = "lobby",
            Title = context.Host.Core.Localize("Tools.Left.Lobby", "大厅"),
            Group = context.Host.Core.Localize("Tools.Left.Multiplayer", "联机"),
            Icon = "lucide/link-2",
            Order = 0,
            Factory = pages.CreateLobbyToolsPage
        });
        _settingsRegistration = ui.ContributeSettingsPanel(new SettingsPanelDescriptor
        {
            Id = "lobby-settings",
            Title = context.Host.Core.Localize("Setup.Left.Item.GameLink", "联机"),
            Group = context.Host.Core.Localize("Setup.Left.Category.Tools", "工具"),
            Icon = "lucide/bubbles",
            Order = 0,
            Factory = pages.CreateLobbySettingsPage
        });

        Log?.Info("EasyTier lobby plugin registered.");
        return Task.CompletedTask;
    }

    public override Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        _settingsRegistration?.Dispose();
        _settingsRegistration = null;
        _toolsRegistration?.Dispose();
        _toolsRegistration = null;
        _serviceRegistration?.Dispose();
        _serviceRegistration = null;
        if (_provider is not null)
        {
            ScaffoldingFactory.ClearProvider(_provider);
            _provider = null;
        }
        _providerRegistration?.Dispose();
        _providerRegistration = null;
        return Task.CompletedTask;
    }
}
