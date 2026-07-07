using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;
using PCL.EasyTierPlugin.Natayark;
using PCL.Plugin.Abstractions;

namespace PCL.EasyTierPlugin.Natayark;

internal static class NatayarkOAuthService
{
    private static readonly Dictionary<string, HttpServer> WebServers = new();

    public static void StartAuthorize(Action? completeCallback = null)
    {
        Exception? resultEx = null;
        StartOAuthWaitingCallback("NatayarkID",
            $"https://account.naids.com/oauth2/authorize?response_type=code&client_id={EasyTierPluginSecrets.Get("NAID_CLIENT_ID")}&redirect_uri=%r",
            (success, parameters, content) =>
            {
                OAuthCompleteStatus? status;
                if (!success)
                {
                    completeCallback?.Invoke();
                    return null;
                }

                var code = parameters["code"];

                try
                {
                    NatayarkProfileManager.GetNaidDataAsync(code, port: ushort.Parse(parameters["Port"])).Wait();
                }
                catch (AggregateException ex)
                {
                    resultEx = ex.InnerExceptions[0];
                }

                if (resultEx is null)
                    status = OAuthCompleteStatus.Complete(NatayarkProfileManager.NaidProfile.Username ?? string.Empty);
                else
                    status = OAuthCompleteStatus.Failed("获取用户信息失败，请尝试重新登录", resultEx);
                completeCallback?.Invoke();
                return status;
            });
    }

    private static bool StartOAuthWaitingCallback(string serviceName, string url, OAuthComplete completeCallback)
    {
        if (IsWebServerRunning(serviceName)) return false;
        Task.Run(() =>
        {
            var serverPort = 0;
            lock (WebServers)
            {
                var server = new NaidCallbackServer(serviceName, completeCallback);
                serverPort = server.Port;
                var webServiceName = $"oauth/{serviceName}";
                if (DisposeWebServer(webServiceName)) LogWrapper.Info("[OAuth] 已关闭先前认证服务服务端");
                StartWebServer(webServiceName, server);
                LogWrapper.Info($"[OAuth] {serviceName}: 初始化完成，开始响应 HTTP 请求");
            }

            OpenWebsite(url.Replace("%r", $"http://localhost:{serverPort}/callback"));
        });
        return true;
    }

    private static bool StartWebServer(string name, HttpServer server)
    {
        name = name.ToLowerInvariant();
        lock (WebServers)
        {
            if (WebServers.ContainsKey(name)) return false;
            WebServers[name] = server;
        }

        Task.Run(() =>
        {
            LogWrapper.Info($"[WebServer] 服务端 '{name}' 已启动");
            try
            {
                server.Start();
                while (true)
                {
                    lock (WebServers)
                    {
                        if (!WebServers.ContainsKey(name)) break;
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "WebServer", $"服务端 '{name}' 运行出错");
            }
            finally
            {
                try
                {
                    server.Dispose();
                }
                catch
                {
                    // Ignore disposal errors.
                }

                LogWrapper.Info($"[WebServer] 服务端 '{name}' 已停止");
                lock (WebServers)
                {
                    WebServers.Remove(name);
                }
            }
        });
        return true;
    }

    private static bool IsWebServerRunning(string name)
    {
        name = name.ToLowerInvariant();
        return WebServers.ContainsKey(name);
    }

    private static bool DisposeWebServer(string name)
    {
        name = name.ToLowerInvariant();
        lock (WebServers)
        {
            if (!WebServers.ContainsKey(name)) return false;
            try
            {
                WebServers[name].Dispose();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            WebServers.Remove(name);
            return true;
        }
    }

    private static void OpenWebsite(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "OAuth", "无法打开 Natayark OAuth 网页");
        }
    }

    private sealed class NaidCallbackServer(string serviceName, OAuthComplete completeCallback)
        : HttpServer([IPAddress.Parse("127.0.0.1")])
    {
        private string _callbackContent = string.Empty;
        private IDictionary<string, string>? _callbackParameters;
        private OAuthCompleteStatus? _status;

        protected override void Init()
        {
            Register(HttpMethod.Get, "/callback", HandleCallback);
            Register(HttpMethod.Post, "/callback", HandleCallback);
            Register(HttpMethod.Get, "/status", HandleStatus);
            Register(HttpMethod.Get, "/complete", HandleComplete);
        }

        private Task<HttpRouteResponse> HandleCallback(HttpListenerRequest request)
        {
            if (!request.IsLocal) return HttpRouteResponse.Forbidden.AsTask();

            var redirect = HttpRouteResponse.Redirect("/complete").AsTask();
            var parameterMap = new Dictionary<string, string>();
            var query = request.Url?.Query ?? string.Empty;
            var queryIndex = query.IndexOf('?');
            if (queryIndex != -1 && query.Length > queryIndex)
            {
                try
                {
                    var sq = query[(queryIndex + 1)..].Split('&');
                    var splitChar = new[] { '=' };
                    foreach (var iq in sq)
                    {
                        var q = iq.Split(splitChar, 2);
                        if (q.Length == 2) parameterMap[q[0]] = q[1];
                    }
                }
                catch (Exception ex)
                {
                    _status = OAuthCompleteStatus.Failed("回调参数解析出错", ex);
                    return redirect;
                }
            }

            _callbackParameters = parameterMap;

            if (request.HasEntityBody)
            {
                try
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    _callbackContent = reader.ReadToEnd();
                }
                catch (Exception ex)
                {
                    _status = OAuthCompleteStatus.Failed("读取回调内容出错", ex);
                    return redirect;
                }
            }

            return redirect;
        }

        private Task<HttpRouteResponse> HandleStatus(HttpListenerRequest request)
        {
            if (_callbackParameters is null) return HttpRouteResponse.NotFound.AsTask();

            try
            {
                if (_status is null)
                {
                    _callbackParameters["Port"] = Port.ToString();
                    _status = completeCallback(true, _callbackParameters, _callbackContent);
                }
                else if (!_status.success)
                {
                    LogWrapper.Info($"[OAuth] {serviceName}: {_status.message}\r\n{_status.stacktrace}");
                    var parameters = new Dictionary<string, string> { ["Port"] = Port.ToString() };
                    completeCallback(false, parameters, _status.message ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _status = OAuthCompleteStatus.Failed("处理回调出错", ex);
            }

            return HttpRouteResponse.Json(_status!).AsTask();
        }

        private static Task<HttpRouteResponse> HandleComplete(HttpListenerRequest request)
        {
            return HttpRouteResponse.Text("<html><body>Natayark ID 登录完成，可以关闭此页面。</body></html>", "text/html").AsTask();
        }
    }

    private sealed class OAuthCompleteStatus
    {
        public bool success { get; set; }
        public string? username { get; set; }
        public string? message { get; set; }
        public string? stacktrace { get; set; }

        public static OAuthCompleteStatus Complete(string username) => new() { success = true, username = username };
        public static OAuthCompleteStatus Failed(string message, Exception ex) => new() { success = false, message = message, stacktrace = ex.ToString() };
    }

    private delegate OAuthCompleteStatus? OAuthComplete(bool success, IDictionary<string, string> parameters, string content);
}
