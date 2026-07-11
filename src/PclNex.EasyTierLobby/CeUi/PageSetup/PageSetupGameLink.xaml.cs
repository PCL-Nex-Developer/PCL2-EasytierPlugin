using System.Windows;
using System.Windows.Controls;
using PCL.Core.App;
using PCL.Core.App.Configuration;
using PCL.Core.App.Localization;
using PCL;

namespace PclNex.EasyTierLobby.UI;

public partial class PageSetupGameLink
{
    private new bool isLoaded;
    private bool _isReloadingUsername;

    public PageSetupGameLink()
    {
        ModBase.Log($"[Link] 插件设置页已实例化：{GetType().AssemblyQualifiedName}");
        InitializeComponent();
        TextLinkUsername.TextChanged += TextBoxChange;
        Loaded += PageSetupLink_Loaded;
    }

    private void PageSetupLink_Loaded(object sender, RoutedEventArgs e)
    {
        // 重复加载部分
        PanBack.ScrollToHome();

        // 非重复加载部分
        if (isLoaded)
            return;
        isLoaded = true;

        Reload();
    }

    public void Reload()
    {
        _isReloadingUsername = true;
        try
        {
            TextLinkUsername.Text = Config.Link.Username;
        }
        finally
        {
            _isReloadingUsername = false;
        }
        CheckLatencyFirstMode.Checked = Config.Link.UseLatencyFirstMode;
        ComboPreferProtocol.SelectedIndex = (int)Config.Link.ProtocolPreference;
        CheckTryPunchSym.Checked = Config.Link.TryPunchSym;
        CheckEnableIPv6.Checked = Config.Link.EnableIPv6;
        CheckEnableCliOutput.Checked = Config.Link.EnableCliOutput;

        // TextRelays.Text = "正在获取信息..."
        // Do While Not (PageLinkLobby.LobbyAnnouncementLoader.State = LoadState.Finished OrElse PageLinkLobby.LobbyAnnouncementLoader.State = LoadState.Failed)
        // Thread.Sleep(500)
        // Loop
        // If ETRelay.RelayList.Count > 0 Then
        // TextRelays.Text = ""
        // For Each Relay In ETRelay.RelayList
        // Select Case Relay.Type
        // Case ETRelayType.Community
        // TextRelays.Text += "[社区] "
        // Case ETRelayType.Selfhosted
        // TextRelays.Text += "[自有] "
        // Case Else 'ETRelayType.Custom
        // TextRelays.Text += "[自定义] "
        // End Select
        // TextRelays.Text += Relay.Name & "，"
        // Next
        // TextRelays.Text = TextRelays.Text.BeforeLast("，")
        // Else
        // TextRelays.Text = "暂无，你可能需要手动添加中继服务器"
        // End If
    }

    // 初始化
    public void Reset()
    {
        try
        {
            Config.Link.Reset();
            ModBase.Log("[Setup] 已初始化联机页设置");
            HintService.Hint(Lang.Text("Setup.GameLink.Initialized"), HintType.Success, false);
            Reload();
        }
        catch (Exception ex)
        {
            ModBase.Log(
                ex,
                Lang.Text("Setup.GameLink.Error.InitFailed"),
                ModBase.LogLevel.Msgbox);
        }

        Reload();
    }

    private void TextBoxChange(object senderRaw, TextChangedEventArgs e)
    {
        var sender = (MyTextBox)senderRaw;
        if (_isReloadingUsername)
            return;

        SetByTag(sender.Tag?.ToString(), sender.Text);
        ModBase.Log($"[Link] 联机用户名已实时更新：{sender.Text}");
    }

    private static void ComboBoxChange(MyComboBox sender, object e)
    {
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.SelectedIndex);
    }

    private void CheckBoxChange(object senderRaw, bool user)
    {
        var sender = (MyCheckBox)senderRaw;
        if (ModAnimation.AniControlEnabled == 0)
            SetByTag(sender.Tag?.ToString(), sender.Checked);
    }

    private static void SetByTag(string tag, object value)
        => LinkConfigCompat.TrySetValue(tag, value);

    private void LinkProtocolPerferenceChange(object sender, SelectionChangedEventArgs e)
    {
        if (ModAnimation.AniControlEnabled == 0)
            try
            {
                var selection = (LinkProtocolPreference)((MyComboBox)sender).SelectedIndex;
                Config.Link.ProtocolPreference = selection;
            }
            catch (Exception ex)
            {
                ModBase.Log(
                    ex,
                    Lang.Text("Setup.GameLink.Error.ConfigChangeFailed"),
                    ModBase.LogLevel.Hint);
            }
    }

}
