using System.Diagnostics;
using PCL;
using PCL.Core.Link.Scaffolding;
using PCL.Core.Link.Scaffolding.Client;
using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.Client.Requests;

var dataDirectory = Path.Combine(Path.GetTempPath(), "PclNex.EasyTierLobby.Smoke");
CeUiRuntimeBridge.Initialize(dataDirectory, null);

var before = Process.GetProcessesByName("easytier-core").Select(process => process.Id).ToHashSet();
var serverEntity = ScaffoldingFactory.CreateServer(25565, "smoke-host");
serverEntity.Server.Start();

Console.WriteLine($"LOBBY_CODE={serverEntity.EasyTier.Lobby.FullCode}");
Console.WriteLine($"HOST_NETWORK_STATE={serverEntity.EasyTier.State}");

await using (var localClient = new ScaffoldingClient(
                 "127.0.0.1",
                 serverEntity.EasyTier.ScaffoldingPort,
                 "local-control",
                 "SMOKE-LOCAL-0001",
                 "smoke"))
{
    using var localCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await localClient.ConnectAsync(localCts.Token);
    var localServerPort = await localClient.SendRequestAsync(new GetServerPortRequest(), localCts.Token);
    Console.WriteLine($"LOCAL_SCAFFOLDING_PORT={localServerPort}");
}

using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var clientEntity = await ScaffoldingFactory.CreateClientAsync(
    "smoke-client",
    serverEntity.EasyTier.Lobby.FullCode,
    LobbyType.Scaffolding,
    joinCts.Token,
    "SMOKE-CLIENT-0001");
await clientEntity.Client.ConnectAsync();
var serverPort = await clientEntity.Client.SendRequestAsync(new GetServerPortRequest());

Console.WriteLine($"CLIENT_NETWORK_STATE={clientEntity.EasyTier.State}");
Console.WriteLine($"SERVER_PORT={serverPort}");

await clientEntity.Client.DisposeAsync();
await clientEntity.EasyTier.StopAsync();
await serverEntity.Server.DisposeAsync();
await serverEntity.EasyTier.StopAsync();
await Task.Delay(500);

var leaked = Process.GetProcessesByName("easytier-core")
    .Where(process => !before.Contains(process.Id))
    .Select(process => process.Id)
    .ToArray();

if (leaked.Length > 0)
{
    throw new InvalidOperationException($"EasyTier processes were not cleaned up: {string.Join(", ", leaked)}");
}

Console.WriteLine("CLEANUP=OK");