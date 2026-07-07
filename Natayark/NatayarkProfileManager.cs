using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.Utils;

namespace PCL.EasyTierPlugin.Natayark;

public class NaidUser
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int Status { get; set; }
    public bool IsRealNamed { get; set; }
    public string? LastIp { get; set; }
}

public static class NatayarkProfileManager
{
    private const string LogModule = "Link";

    public static NaidUser NaidProfile { get; private set; } = new();

    private static Task? _getNaidData;

    public static async Task GetNaidDataAsync(string token, bool isRefresh = false, bool isRetry = false, ushort port = 0)
    {
        if (_getNaidData is not null && !_getNaidData.IsCompleted)
        {
            await _getNaidData.ConfigureAwait(false);
            return;
        }

        _getNaidData = Task.Run(async () =>
        {
            try
            {
                var requestData = new List<KeyValuePair<string, string>>
                {
                    new("grant_type", isRefresh ? "refresh_token" : "authorization_code"),
                    new("client_id", EasyTierPluginSecrets.Get("NAID_CLIENT_ID") ?? string.Empty),
                    new("client_secret", EasyTierPluginSecrets.Get("NAID_CLIENT_SECRET") ?? string.Empty),
                    new(isRefresh ? "refresh_token" : "code", token)
                };
                if (!isRefresh)
                    requestData.Add(new KeyValuePair<string, string>("redirect_uri", $"http://localhost:{port}/callback"));

                using var httpContent = new FormUrlEncodedContent(requestData);

                using var oauthResponse = await HttpRequest
                    .CreatePost("https://account.naids.com/api/oauth2/token")
                    .WithContent(httpContent)
                    .SendAsync()
                    .ConfigureAwait(false);
                oauthResponse.EnsureSuccessStatusCode();

                var result = await oauthResponse.AsStringAsync().ConfigureAwait(false)
                    ?? throw new Exception(Lang.Text("Link.Natayark.TokenFetchEmpty"));
                var data = JsonCompat.ParseNode(result);
                var accessToken = data?["access_token"]?.ToString();
                var refreshToken = data?["refresh_token"]?.ToString();

                if (data is null || accessToken is null || refreshToken is null)
                    throw new Exception(Lang.Text("Link.Natayark.TokenParseFailed"));

                NaidProfile.AccessToken = accessToken;
                NaidProfile.RefreshToken = refreshToken;

                var expiresAt = data["refresh_token_expires_at"]!.ToString();

                using var userDataResponse = await HttpRequest
                    .Create("https://account.naids.com/api/api/user/data")
                    .WithBearerToken(NaidProfile.AccessToken)
                    .SendAsync()
                    .ConfigureAwait(false);
                userDataResponse.EnsureSuccessStatusCode();

                var receivedUserData = await userDataResponse.AsStringAsync().ConfigureAwait(false)
                    ?? throw new Exception(Lang.Text("Link.Natayark.UserInfoFetchEmpty"));
                var userData = JsonCompat.ParseNode(receivedUserData)?["data"]
                    ?? throw new Exception(Lang.Text("Link.Natayark.UserInfoParseFailed"));

                NaidProfile.Id = JsonCompat.ToObject<int>(userData["id"]);
                NaidProfile.Username = userData["username"]?.ToString() ?? string.Empty;
                NaidProfile.Email = userData["email"]?.ToString() ?? string.Empty;
                NaidProfile.Status = JsonCompat.ToObject<int>(userData["status"]);
                NaidProfile.IsRealNamed = JsonCompat.ToObject<bool>(userData["realname"]);
                NaidProfile.LastIp = userData["last_ip"]?.ToString() ?? string.Empty;

                EasyTierPluginState.NaidRefreshToken = NaidProfile.RefreshToken;
                EasyTierPluginState.NaidRefreshExpireTime = expiresAt;
            }
            catch (Exception ex)
            {
                if (isRetry)
                {
                    NaidProfile = new NaidUser();
                    EasyTierPluginState.NaidRefreshToken = string.Empty;
                    WarnLog(Lang.Text("Tools.GameLink.Natayark.ProfileLoadFailed"));
                }
                else
                {
                    if (ex.Message.Contains("invalid access token"))
                    {
                        WarnLog(Lang.Text("Tools.GameLink.Natayark.TokenInvalid"));
                        await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                        await GetNaidDataAsync(EasyTierPluginState.NaidRefreshToken, true, true).ConfigureAwait(false);
                    }
                    else if (ex.Message.Contains("invalid_grant"))
                    {
                        WarnLog(Lang.Text("Tools.GameLink.Natayark.InvalidAuthCode"));
                    }
                    else if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
                    {
                        NaidProfile = new NaidUser();
                        EasyTierPluginState.NaidRefreshToken = string.Empty;
                        WarnLog(Lang.Text("Tools.GameLink.Natayark.AccountExpired"));
                    }
                    else
                    {
                        NaidProfile = new NaidUser();
                        EasyTierPluginState.NaidRefreshToken = string.Empty;
                        WarnLog(Lang.Text("Tools.GameLink.Natayark.LoginFailed"));
                    }
                }
                throw;

                void WarnLog(string msg)
                {
                    LogWrapper.Warn(ex, LogModule, msg);
                    HintWrapper.Show(msg, HintTheme.Error);
                }
            }
        });

        await _getNaidData.ConfigureAwait(false);
    }

    public static void ClearProfile() => NaidProfile = new NaidUser();
}
