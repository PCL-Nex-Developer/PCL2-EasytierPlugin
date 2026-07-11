using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PclNex.EasyTierLobby.Services;

internal sealed class LobbyTelemetryService(IPluginLog log)
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<bool> SendAsync(
        bool isHost,
        string machineId,
        string customName,
        NatayarkProfile profile,
        LobbyAnnouncement announcement,
        CancellationToken cancellationToken = default)
    {
        var key = OriginalSecrets.TelemetryKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            if (announcement.RequireLogin)
            {
                log.Warn("联机数据发送失败，未设置 PCL_TELEMETRY_KEY。");
                return false;
            }

            log.Warn("联机数据发送失败，未设置 PCL_TELEMETRY_KEY，跳过发送。");
            return true;
        }

        var servers = string.Join(';', announcement.Relays.Select(relay => relay.Url));
        var data = new JsonObject
        {
            ["Tag"] = "Link",
            ["Id"] = machineId,
            ["NaidId"] = profile.Id,
            ["NaidEmail"] = profile.Email,
            ["NaidLastIp"] = profile.LastIp,
            ["CustomName"] = customName,
            ["Servers"] = servers,
            ["IsHost"] = isHost
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://pcl2ce.pysio.online/post")
        {
            Content = new StringContent(new JsonObject { ["data"] = data }.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            log.Warn($"联机数据发送失败，状态码 {(int)response.StatusCode}。");
            return !announcement.RequireLogin;
        }
        catch (Exception ex)
        {
            log.Error(ex, "联机数据发送失败。");
            return !announcement.RequireLogin;
        }
    }
}
