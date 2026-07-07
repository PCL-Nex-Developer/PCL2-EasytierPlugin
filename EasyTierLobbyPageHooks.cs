using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCL.EasyTierPlugin.EasyTier;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin;

internal sealed class EasyTierLobbyPageHooks(IPluginContext context, IPluginLogger log)
{
    private readonly HashSet<FrameworkElement> _hookedToolsPages = [];
    private readonly HashSet<FrameworkElement> _hookedSettingsPages = [];

    public FrameworkElement CreateToolsPage(Func<FrameworkElement> pageFactory)
    {
        if (IsPluginManagementPreviewCall()) return CreatePreviewPanel(Text("EasyTier.Plugin.ToolsPreview", "请从 工具 -> 大厅 进入 EasyTier 联机大厅。"));
        return HookToolsPage(pageFactory());
    }

    public FrameworkElement CreateSettingsPage(Func<FrameworkElement> pageFactory)
    {
        if (IsPluginManagementPreviewCall()) return CreatePreviewPanel(Text("EasyTier.Plugin.SettingsPreview", "请从 设置 -> 联机 管理 EasyTier 联机配置。"));
        return HookSettingsPage(pageFactory());
    }

    public FrameworkElement HookToolsPage(FrameworkElement page)
    {
        if (_hookedToolsPages.Add(page))
        {
            page.Loaded += (_, _) => HookToolsPageControls(page);
            HookToolsPageControls(page);
        }

        return page;
    }

    public FrameworkElement HookSettingsPage(FrameworkElement page)
    {
        if (_hookedSettingsPages.Add(page))
        {
            page.Loaded += (_, _) => HookSettingsPageControls(page);
            HookSettingsPageControls(page);
        }

        return page;
    }

    private void HookToolsPageControls(FrameworkElement page)
    {
        if (page.FindName("BtnNatTest") is not UIElement button || button.GetValue(HookAttachedProperty) is true) return;

        button.SetValue(HookAttachedProperty, true);
        button.PreviewMouseLeftButtonUp += async (_, args) =>
        {
            args.Handled = true;
            await RunToolsNetworkTestAsync(page).ConfigureAwait(false);
        };
    }

    private void HookSettingsPageControls(FrameworkElement page)
    {
        if (page.FindName("BtnNetTest") is not UIElement button || button.GetValue(HookAttachedProperty) is true) return;

        button.SetValue(HookAttachedProperty, true);
        button.PreviewMouseLeftButtonUp += async (_, args) =>
        {
            args.Handled = true;
            await RunSettingsNetworkTestAsync(page).ConfigureAwait(false);
        };
    }

    private async Task RunToolsNetworkTestAsync(FrameworkElement page)
    {
        await InvokeOnUiAsync(() =>
        {
            if (page.FindName("BtnNatTest") is UIElement button) button.IsEnabled = false;
            SetText(page, "LabNatType", Text("Tools.GameLink.Nat.Testing", "正在测试"));
        }).ConfigureAwait(false);

        try
        {
            var status = await RunNetworkTestAsync().ConfigureAwait(false);
            await InvokeOnUiAsync(() =>
            {
                if (status is null)
                {
                    SetText(page, "LabNatType", Text("Tools.GameLink.Nat.Failed", "测试失败"));
                    context.Host.Core.Hint(Text("Setup.GameLink.NetworkTest.Failed", "网络测试失败。"), PluginHintType.Error);
                    return;
                }

                SetText(page, "LabNatType", string.Format(
                    Text("Tools.GameLink.Nat.Result", "UDP: {0}, TCP: {1}"),
                    GetNatTypeString(status.UdpNatType),
                    GetNatTypeString(status.TcpNatType)));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warn("Failed to run EasyTier network test.", ex);
            await InvokeOnUiAsync(() =>
            {
                SetText(page, "LabNatType", Text("Tools.GameLink.Nat.Failed", "测试失败"));
                context.Host.Core.Hint(Text("Setup.GameLink.NetworkTest.Failed", "网络测试失败。"), PluginHintType.Error);
            }).ConfigureAwait(false);
        }
        finally
        {
            await InvokeOnUiAsync(() =>
            {
                if (page.FindName("BtnNatTest") is UIElement button) button.IsEnabled = true;
            }).ConfigureAwait(false);
        }
    }

    private async Task RunSettingsNetworkTestAsync(FrameworkElement page)
    {
        await InvokeOnUiAsync(() =>
        {
            if (page.FindName("BtnNetTest") is Control button)
            {
                button.IsEnabled = false;
                button.SetValue(ContentControl.ContentProperty, Text("Setup.GameLink.NetworkTest.Testing", "正在测试"));
            }
        }).ConfigureAwait(false);

        try
        {
            var status = await RunNetworkTestAsync().ConfigureAwait(false);
            await InvokeOnUiAsync(() =>
            {
                if (status is null)
                {
                    context.Host.Core.Hint(Text("Setup.GameLink.NetworkTest.Failed", "网络测试失败。"), PluginHintType.Error);
                    return;
                }

                SetText(page, "TextUdpNatType", string.Format(
                    Text("Setup.GameLink.NetworkTest.UdpNatType", "UDP NAT 类型: {0}"),
                    GetNatTypeString(status.UdpNatType)));
                SetText(page, "TextTcpNatType", string.Format(
                    Text("Setup.GameLink.NetworkTest.TcpNatType", "TCP NAT 类型: {0}"),
                    GetNatTypeString(status.TcpNatType)));
                SetText(page, "TextIpv6Status", string.Format(
                    Text("Setup.GameLink.NetworkTest.Ipv6Status", "IPv6: {0}"),
                    status.SupportIPv6
                        ? Text("Setup.GameLink.NetworkTest.Supported", "支持")
                        : Text("Setup.GameLink.NetworkTest.Unsupported", "不支持")));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warn("Failed to run EasyTier network test.", ex);
            await InvokeOnUiAsync(() => context.Host.Core.Hint(
                Text("Setup.GameLink.NetworkTest.Failed", "网络测试失败。"),
                PluginHintType.Error)).ConfigureAwait(false);
        }
        finally
        {
            await InvokeOnUiAsync(() =>
            {
                if (page.FindName("BtnNetTest") is Control button)
                {
                    button.IsEnabled = true;
                    button.SetValue(ContentControl.ContentProperty, Text("Setup.GameLink.NetworkTest.Start", "开始测试"));
                }
            }).ConfigureAwait(false);
        }
    }

    private async Task<CliNetTest.NetStatus?> RunNetworkTestAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await EasyTierDependencyManager.EnsureReadyAsync(context, log, cts.Token).ConfigureAwait(false);
        return await CliNetTest.GetNetStatusAsync().ConfigureAwait(false);
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (context.Host.Ui?.CheckAccess() == true)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Host.Ui?.InvokeOnUi(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private string GetNatTypeString(CliNetTest.NatType type)
    {
        var key = type switch
        {
            CliNetTest.NatType.OpenInternet or CliNetTest.NatType.NoPat => "Link.Nat.Type.Open",
            CliNetTest.NatType.FullCone => "Link.Nat.Type.FullCone",
            CliNetTest.NatType.PortRestricted => "Link.Nat.Type.PortRestricted",
            CliNetTest.NatType.Restricted => "Link.Nat.Type.Restricted",
            CliNetTest.NatType.SymmetricEasy => "Link.Nat.Type.SymmetricEasy",
            CliNetTest.NatType.Symmetric => "Link.Nat.Type.Symmetric",
            CliNetTest.NatType.SymmetricFirewall => "Link.Nat.Type.SymmetricFirewall",
            CliNetTest.NatType.UdpBlocked => "Link.Nat.Type.UdpBlocked",
            _ => "Link.Nat.Type.Unknown"
        };

        return Text(key, CliNetTest.GetNatTypeString(type));
    }

    private string Text(string key, string fallback)
    {
        var value = context.Host.Core.Localize(key, fallback);
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static bool IsPluginManagementPreviewCall()
    {
        foreach (var frame in new StackTrace().GetFrames() ?? [])
        {
            var typeName = frame.GetMethod()?.DeclaringType?.FullName;
            if (typeName is "PCL.PluginTabHost" or "PCL.PagePluginsInstalled") return true;
        }

        return false;
    }

    private static FrameworkElement CreatePreviewPanel(string message)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private static void SetText(FrameworkElement page, string name, string text)
    {
        if (page.FindName(name) is TextBlock textBlock) textBlock.Text = text;
        else if (page.FindName(name) is ContentControl contentControl) contentControl.Content = text;
        else if (page.FindName(name) is Control control) control.SetValue(ContentControl.ContentProperty, text);
    }

    private static readonly DependencyProperty HookAttachedProperty = DependencyProperty.RegisterAttached(
        "EasyTierNetworkTestHooked",
        typeof(bool),
        typeof(EasyTierLobbyPageHooks),
        new PropertyMetadata(false));
}