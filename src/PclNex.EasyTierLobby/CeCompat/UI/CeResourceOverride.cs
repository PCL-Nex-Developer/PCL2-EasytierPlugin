using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace PCL;

public static class CeResourceOverride
{
    private const string ResourceName = "PclNex.EasyTierLobby.Resources.ce-zh-CN.xaml";

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
    }

    private static bool ShouldOverride(string key)
    {
        return key.StartsWith("Tools.GameLink.", StringComparison.Ordinal)
            || key.StartsWith("Setup.GameLink.", StringComparison.Ordinal)
            || key is "Tools.Left.Lobby" or "Tools.Left.Multiplayer" or "Setup.Left.Item.GameLink" or "Setup.Left.Category.Tools";
    }
}