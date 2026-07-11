using System;
using System.Threading;
using System.Threading.Tasks;
using PCL.Plugin.Abstractions;
using PclNex.EasyTierLobby.Services;
using PclNex.EasyTierLobby.UI;

namespace PclNex.EasyTierLobby;

[Plugin(
    id: PluginIds.Id,
    name: PluginIds.Name,
    version: PluginIds.Version,
    Author = "Nex(XueLing)",
    Description = "实现 EasyTier 联机大厅功能。",
    MinApiVersion = "1.2.1.0",
    Capabilities = PluginCapabilities.ContributeTools | PluginCapabilities.ContributeSettings,
    LoadTiming = PluginLoadTiming.WindowCreated)]
public sealed class EasyTierLobbyPlugin : PclPluginBase
{
    private IDisposable? _toolsPanel;
    private IDisposable? _settingsPanel;
    private PageToolsGameLink? _toolsPage;
    private PageSetupGameLink? _settingsPage;

    public override Task LoadAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        base.LoadAsync(context, cancellationToken);
        PCL.CeUiRuntimeBridge.Initialize(context.DataDirectory, Log);
        PCL.CeResourceOverride.Apply();
        PCL.Core.Link.Scaffolding.EasyTier.EasyTierEntity.CleanupOrphanedLobbyProcesses();

        var ui = context.Host.Ui ?? throw new InvalidOperationException("UI API is unavailable.");
        _toolsPanel = ui.ContributeToolsPanel(new ToolsPanelDescriptor
        {
            Id = "easytier-lobby",
            Title = "大厅",
            Group = "联机",
            Icon = "lucide/link-2",
            Order = 10,
            Factory = () => _toolsPage ??= new PageToolsGameLink()
        });

        _settingsPanel = ui.ContributeSettingsPanel(new SettingsPanelDescriptor
        {
            Id = "game-link",
            Title = "联机",
            Group = "工具",
            Icon = "lucide/bubbles",
            Order = 70,
            Factory = () => _settingsPage ??= new PageSetupGameLink()
        });

        Log?.Info("PCL.Nex EasyTier lobby panels registered.");
        return Task.CompletedTask;
    }

    public override async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        await PCL.Core.Link.Lobby.LobbyService.LeaveLobbyAsync().ConfigureAwait(false);
        _settingsPanel?.Dispose();
        _settingsPanel = null;
        _settingsPage = null;
        _toolsPanel?.Dispose();
        _toolsPanel = null;
        _toolsPage = null;
    }
}

internal static class PluginIds
{
    public const string Id = "pclnex.easytier";
    public const string Name = "EasyTier 联机大厅";
    public const string Version = ThisAssembly.PluginVersion;
}
