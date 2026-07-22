using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace PCL;

public static class CeResourceOverride
{
    private const string ResourceName = "PclNex.EasyTierLobby.Resources.ce-zh-CN.xaml";
    private static readonly IReadOnlyDictionary<string, string> RequiredServerQueryResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tools.ServerQuery.Action.Query"] = "查询",
            ["Tools.ServerQuery.Address.Hint"] = "输入服务器地址",
            ["Tools.ServerQuery.State.NoInfo"] = "未返回服务器信息",
            ["Tools.ServerQuery.State.Querying"] = "查询中...",
            ["Tools.ServerQuery.Title.MinecraftServer"] = "Minecraft 服务器",
            ["Tools.ServerQuery.Error.AddressTooLong"] = "服务器地址过长",
            ["Tools.ServerQuery.Error.EmptyPacket"] = "服务器返回了空数据包",
            ["Tools.ServerQuery.Error.IncompleteConnection"] = "服务器在返回完整状态数据包前提前断开连接。",
            ["Tools.ServerQuery.Error.InvalidResponse"] = "服务器返回了错误的信息",
            ["Tools.ServerQuery.Error.PongDataLength"] = "Pong 数据长度必须为 8 字节",
            ["Tools.ServerQuery.Error.StaleConnection"] = "服务器在返回状态后提前断开连接，未返回 pong 数据包，无法计算延迟。",
            ["Tools.ServerQuery.Error.Timeout.Connect"] = "连接超时",
            ["Tools.ServerQuery.Error.Timeout.ReadWrite"] = "数据读写超时",
            ["Tools.ServerQuery.Error.UnableToConnect"] = "无法连接: {0}",
            ["Tools.Test.ServerQuery.Title"] = "瞅眼服务器"
        };

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null)
            return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Apply);
            return;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return;

        if (XamlReader.Load(stream) is not ResourceDictionary ceResources)
            return;

        foreach (DictionaryEntry entry in ceResources)
        {
            if (entry.Key is not string key || !ShouldOverride(key))
                continue;
            app.Resources[key] = entry.Value ?? string.Empty;
        }

        foreach (var entry in RequiredServerQueryResources)
            app.Resources[entry.Key] = entry.Value;
    }

    private static bool ShouldOverride(string key)
    {
        return key.StartsWith("Tools.GameLink.", StringComparison.Ordinal)
            || key.StartsWith("Tools.ServerQuery.", StringComparison.Ordinal)
            || key.StartsWith("Setup.GameLink.", StringComparison.Ordinal)
            || key is "Tools.Left.Lobby" or "Tools.Left.Multiplayer" or "Tools.Test.ServerQuery.Title"
                or "Setup.Left.Item.GameLink" or "Setup.Left.Category.Tools";
    }
}
