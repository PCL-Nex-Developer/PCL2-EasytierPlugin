using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PclNex.EasyTierLobby.Services;

internal sealed class NatayarkAuthService(string dataDirectory, IPluginLog log)
{
    private readonly HttpClient _httpClient = new();
    private readonly string _tokenFile = Path.Combine(dataDirectory, "natayark-refresh-token.txt");
    private readonly string _expireFile = Path.Combine(dataDirectory, "natayark-refresh-expire.txt");

    public NatayarkProfile Profile { get; private set; } = new();

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(ReadRefreshToken());

    public async Task<NatayarkProfile> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var token = ReadRefreshToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("请先登录 Natayark 账户。");
        }

        var expires = ReadRefreshExpireTime();
        if (expires is not null && expires.Value < DateTime.Now)
        {
            Logout();
            throw new InvalidOperationException("Natayark 登录已过期，请重新登录。");
        }

        return await ExchangeTokenAsync(token, refresh: true, callbackPort: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NatayarkProfile> LoginWithBrowserAsync(CancellationToken cancellationToken = default)
    {
        var clientId = OriginalSecrets.NatayarkClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("缺少 Natayark client id。");
        }

        var port = NetworkPorts.GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/callback/");
        listener.Start();

        var redirect = Uri.EscapeDataString($"http://localhost:{port}/callback");
        var url = $"https://account.naids.com/oauth2/authorize?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={redirect}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));
        var context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        var code = context.Request.QueryString["code"];
        var html = Encoding.UTF8.GetBytes("<html><body>Natayark login complete. You can return to PCL.Nex.</body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = html.Length;
        await context.Response.OutputStream.WriteAsync(html, timeoutCts.Token).ConfigureAwait(false);
        context.Response.Close();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Natayark 回调没有返回授权代码。");
        }

        return await ExchangeTokenAsync(code, refresh: false, callbackPort: port, cancellationToken).ConfigureAwait(false);
    }

    public void Logout()
    {
        Profile = new NatayarkProfile();
        File.Delete(_tokenFile);
        File.Delete(_expireFile);
    }

    private async Task<NatayarkProfile> ExchangeTokenAsync(string token, bool refresh, int? callbackPort, CancellationToken cancellationToken)
    {
        var clientId = OriginalSecrets.NatayarkClientId;
        var clientSecret = OriginalSecrets.NatayarkClientSecret;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("缺少 Natayark client secret：请检查 OriginalSecrets 内置配置或 PCL_NAID_CLIENT_SECRET 环境变量。");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", refresh ? "refresh_token" : "authorization_code"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
            new(refresh ? "refresh_token" : "code", token)
        };
        if (!refresh && callbackPort is not null)
        {
            form.Add(new KeyValuePair<string, string>("redirect_uri", $"http://localhost:{callbackPort}/callback"));
        }

        using var tokenResponse = await _httpClient.PostAsync(
            "https://account.naids.com/api/oauth2/token",
            new FormUrlEncodedContent(form),
            cancellationToken).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var tokenData = JsonSerializer.Deserialize<NatayarkTokenResponse>(tokenJson, JsonOptions())
                        ?? throw new InvalidOperationException("Natayark token response is empty.");
        if (string.IsNullOrWhiteSpace(tokenData.AccessToken) || string.IsNullOrWhiteSpace(tokenData.RefreshToken))
        {
            throw new InvalidOperationException("Natayark token response is invalid.");
        }

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://account.naids.com/api/api/user/data");
        userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
        using var userResponse = await _httpClient.SendAsync(userRequest, cancellationToken).ConfigureAwait(false);
        userResponse.EnsureSuccessStatusCode();
        var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var userEnvelope = JsonSerializer.Deserialize<NatayarkUserEnvelope>(userJson, JsonOptions())
                           ?? throw new InvalidOperationException("Natayark user response is empty.");
        var user = userEnvelope.Data ?? throw new InvalidOperationException("Natayark user response has no data.");

        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(_tokenFile, tokenData.RefreshToken);
        if (!string.IsNullOrWhiteSpace(tokenData.RefreshTokenExpiresAt))
        {
            File.WriteAllText(_expireFile, tokenData.RefreshTokenExpiresAt);
        }

        Profile = new NatayarkProfile
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Username = user.Username ?? string.Empty,
            AccessToken = tokenData.AccessToken,
            RefreshToken = tokenData.RefreshToken,
            Status = user.Status,
            IsRealNamed = user.RealName,
            LastIp = user.LastIp ?? string.Empty
        };

        log.Info($"Natayark profile loaded: {Profile.Username}");
        return Profile;
    }

    private string? ReadRefreshToken() => File.Exists(_tokenFile) ? File.ReadAllText(_tokenFile).Trim() : null;

    private DateTime? ReadRefreshExpireTime()
    {
        if (!File.Exists(_expireFile))
        {
            return null;
        }

        return DateTime.TryParse(File.ReadAllText(_expireFile).Trim(), out var value) ? value : null;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed record NatayarkProfile
{
    public int Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int Status { get; init; }
    public bool IsRealNamed { get; init; }
    public string LastIp { get; init; } = string.Empty;
}

internal sealed record NatayarkTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("refresh_token_expires_at")]
    public string? RefreshTokenExpiresAt { get; init; }
}

internal sealed record NatayarkUserEnvelope
{
    [JsonPropertyName("data")]
    public NatayarkUserData? Data { get; init; }
}

internal sealed record NatayarkUserData
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("realname")]
    public bool RealName { get; init; }

    [JsonPropertyName("last_ip")]
    public string? LastIp { get; init; }
}
