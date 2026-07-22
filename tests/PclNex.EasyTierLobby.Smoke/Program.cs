using System.Reflection;
using System.Text.Json;
using PCL.Core.App.Plugins;
using PCL.Core.Link.Scaffolding;
using PCL.Mixin;
using PclNex.EasyTierLobby.UI;

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

for (var index = 0; index < 50; index++)
{
    var lobby = LobbyCodeGenerator.Generate();
    Assert(LobbyCodeGenerator.TryParse(lobby.FullCode, out var parsed), "Generated lobby code did not parse.");
    Assert(parsed == lobby, "Lobby code round trip changed its payload.");
}
Console.WriteLine("LOBBY_CODE_ROUNDTRIP=OK");

using var pluginJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "plugin.json")));
Assert(pluginJson.RootElement.GetProperty("entryAssembly").GetString() == "lib/PCL.EasyTierPlugin.dll", "plugin.json entryAssembly is not the new layout.");
var mixinConfigs = pluginJson.RootElement.GetProperty("mixinConfigs").EnumerateArray()
    .Select(element => element.GetString())
    .ToArray();
Assert(mixinConfigs.SequenceEqual(["mixins/pclnex.easytier.mixins.json"]), "plugin.json mixinConfigs is invalid.");
Assert(!pluginJson.RootElement.TryGetProperty("capabilities", out _), "Legacy capabilities remain in plugin.json.");
Assert(!pluginJson.RootElement.TryGetProperty("runtime", out _), "Legacy runtime remains in plugin.json.");

using var mixinJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "mixins", "pclnex.easytier.mixins.json")));
Assert(mixinJson.RootElement.GetProperty("package").GetString() == "PclNex.EasyTierLobby.Mixins", "Mixin package is invalid.");
var declaredMixins = mixinJson.RootElement.GetProperty("mixins").EnumerateArray()
    .Select(element => element.GetString())
    .ToArray();
Assert(declaredMixins.SequenceEqual(["PageToolsLeftMixin", "PageSetupLeftMixin", "PageToolsTestMixin"]), "CE page mixins are not declared in injection order.");

var marketManifest = JsonSerializer.Deserialize<PluginMarketManifest>(
    File.ReadAllText(Path.Combine(repositoryRoot, "manifest.json")),
    PluginJson.SerializerOptions) ?? throw new InvalidOperationException("manifest.json is empty.");
PluginRepositoryService.ValidateMarketManifest(marketManifest);
Assert(marketManifest.Versions.Count == 1 && marketManifest.Versions[0].Downloads?.AnyCpu is not null,
    "manifest.json does not declare the current anycpu package.");
Console.WriteLine("MARKET_MANIFEST=OK");

var pluginAssembly = typeof(PageToolsGameLink).Assembly;
var references = pluginAssembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(references.Contains("PCL.Core"), "Plugin does not reference PCL.Core.");
Assert(references.Contains("Plain Craft Launcher 2"), "Plugin does not bind the copied CE pages to the Nex host controls.");
Assert(!references.Contains("PCL.Plugin.Abstractions"), "Plugin still references the legacy plugin contract.");

Assert(typeof(PageToolsGameLink).BaseType?.FullName == "PCL.MyPageRight", "Copied CE lobby page does not use the host MyPageRight.");
Assert(typeof(PageSetupGameLink).BaseType?.FullName == "PCL.MyPageRight", "Copied CE settings page does not use the host MyPageRight.");
Assert(pluginAssembly.GetType("PclNex.EasyTierLobby.UI.EasyTierMainPage") is null, "The old custom main page still exists.");
Assert(pluginAssembly.GetType("PclNex.EasyTierLobby.UI.PluginPageHost") is null, "The old custom page host still exists.");
Assert(pluginAssembly.GetType("PclNex.EasyTierLobby.Mixins.FormMainMixin") is null, "The obsolete top navigation mixin still exists.");

AssertMixin(pluginAssembly, "PageToolsLeftMixin", "PCL.PageToolsLeft", [".ctor", "PageLinkLeft_Loaded", "PageGet"]);
AssertMixin(pluginAssembly, "PageSetupLeftMixin", "PCL.PageSetupLeft", [".ctor", "PageGet"]);
AssertMixin(pluginAssembly, "PageToolsTestMixin", "PCL.PageToolsTest", [".ctor"]);
Console.WriteLine("MIXIN_CONTRACT=OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertMixin(Assembly assembly, string name, string targetName, string[] injectedMethods)
{
    var mixinType = assembly.GetType($"PclNex.EasyTierLobby.Mixins.{name}")
                    ?? throw new InvalidOperationException($"{name} is missing.");
    var mixinAttribute = mixinType.GetCustomAttribute<MixinAttribute>();
    Assert(mixinAttribute?.TargetName == targetName, $"{name} target is invalid.");
    var actualMethods = mixinType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .SelectMany(method => method.GetCustomAttributes<InjectAttribute>())
        .Select(attribute => attribute.Method)
        .ToHashSet(StringComparer.Ordinal);
    Assert(injectedMethods.All(actualMethods.Contains), $"{name} is missing an expected injection.");
}
