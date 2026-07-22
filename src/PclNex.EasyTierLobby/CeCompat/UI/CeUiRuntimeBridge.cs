using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net.Http;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;
using PCL.Core.Link.Natayark;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.UI;
using PclNex.EasyTierLobby.Services;

namespace PCL;

public static class CeUiRuntimeBridge
{
    public static string DataDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PclNex.EasyTierLobby");

    public static void Initialize(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        LinkPersistence.Initialize(DataDirectory);
        OriginalSecrets.ApplyToProcessEnvironment();
        MigrateEasyTierFiles();
        Environment.SetEnvironmentVariable("PCLNEX_EASYTIER_DIR", EasyTierDirectory);
    }

    public static string EasyTierDirectory => EasyTierMetadata.EasyTierFilePath;

    private static void MigrateEasyTierFiles()
    {
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        var legacyDirectory = Path.Combine(
            DataDirectory,
            "EasyTier",
            EasyTierMetadata.CurrentEasyTierVer,
            $"easytier-windows-{architecture}");

        if (Directory.Exists(EasyTierDirectory) || !Directory.Exists(legacyDirectory))
            return;

        Directory.CreateDirectory(EasyTierDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(legacyDirectory))
        {
            File.Copy(sourcePath, Path.Combine(EasyTierDirectory, Path.GetFileName(sourcePath)), overwrite: true);
        }

        ModBase.Log($"[Link] 已迁移 EasyTier 文件：{EasyTierDirectory}");
    }
}

public static class ModWebServerCompat
{
    private static readonly Dictionary<string, HttpServer> _webServers = new();
    private static readonly object changeLock = new();
    private static string? picAddress;

    public static bool StartWebServer(string name, HttpServer server)
    {
        name = name.ToLowerInvariant();
        lock (_webServers)
        {
            if (_webServers.ContainsKey(name))
                return false;
            _webServers[name] = server;
        }

        Task.Run(() =>
        {
            ModBase.Log($"[WebServer] 服务端 '{name}' 已启动");
            try
            {
                server.Start();
                while (true)
                {
                    lock (_webServers)
                    {
                        if (!_webServers.ContainsKey(name))
                            break;
                    }

                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                ModBase.Log(ex, $"[WebServer] 服务端 '{name}' 运行出错");
            }
            finally
            {
                try
                {
                    server.Dispose();
                }
                catch
                {
                    // ignored
                }

                ModBase.Log($"[WebServer] 服务端 '{name}' 已停止");
                lock (_webServers)
                {
                    _webServers.Remove(name);
                }
            }
        });
        return true;
    }

    public static bool IsWebServerRunning(string name)
    {
        name = name.ToLowerInvariant();
        return _webServers.ContainsKey(name);
    }

    public static bool DisposeWebServer(string name)
    {
        name = name.ToLowerInvariant();
        lock (_webServers)
        {
            if (!_webServers.ContainsKey(name))
                return false;
            try
            {
                _webServers[name].Dispose();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            _webServers.Remove(name);
            return true;
        }
    }

    public class NaidCallbackServer : HttpServer
    {
        private readonly OAuthComplete _completeCallback;
        private readonly string? _picAddress;
        private readonly string _serviceName;
        private string? _callbackContent;
        private IDictionary<string, string>? _callbackParameters;

        private OAuthCompleteStatus? _status;

        public NaidCallbackServer(string serviceName, OAuthComplete completeCallback, string? picAddress) : base(new[]
            { IPAddress.Parse("127.0.0.1") })
        {
            _serviceName = serviceName;
            _completeCallback = completeCallback;
            _picAddress = picAddress;
        }

        protected override void Init()
        {
            Register(HttpMethod.Get, "/callback", HandleCallback);
            Register(HttpMethod.Post, "/callback", HandleCallback);
            Register(HttpMethod.Get, "/status", HandleStatus);
            Register(HttpMethod.Get, "/assets/background", HandleBackground);
            Register(HttpMethod.Get, "/assets/icon", HandleIcon);
            Register(HttpMethod.Get, "/complete", HandleComplete);
        }

        private Task<HttpRouteResponse> HandleCallback(HttpListenerRequest request)
        {
            if (!request.IsLocal)
                return HttpRouteResponse.Forbidden.AsTask();

            var redirect = HttpRouteResponse.Redirect("/complete").AsTask();

            var parameterMap = new Dictionary<string, string>();
            var query = request.Url?.Query ?? string.Empty;
            var queryIndex = query.IndexOf('?');
            if (queryIndex != -1 && query.Length > queryIndex)
                try
                {
                    var sq = query.Substring(queryIndex + 1).Split('&');
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

            _callbackParameters = parameterMap;

            if (request.HasEntityBody)
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

            return redirect;
        }

        private Task<HttpRouteResponse> HandleStatus(HttpListenerRequest request)
        {
            if (_callbackParameters is null)
                return HttpRouteResponse.NotFound.AsTask();

            try
            {
                if (_status is null)
                {
                    _callbackParameters["Port"] = Port.ToString();
                    _status = _completeCallback(true, _callbackParameters, _callbackContent);
                }
                else if (!_status.success)
                {
                    ModBase.Log($"[OAuth] {_serviceName}: {_status.message}{"\r\n"}{_status.stacktrace}");
                    var pa = new Dictionary<string, string>();
                    pa["Port"] = Port.ToString();
                    _completeCallback(false, pa, _status.message ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _status = OAuthCompleteStatus.Failed("处理回调出错", ex);
            }

            return HttpRouteResponse.Json(_status).AsTask();
        }

        private Task<HttpRouteResponse> HandleBackground(HttpListenerRequest request)
        {
            if (string.IsNullOrWhiteSpace(_picAddress))
                return Task.FromResult(HttpRouteResponse.NotFound);
            return HttpRouteResponse
                .Input(new FileStream(_picAddress, FileMode.Open, FileAccess.Read, FileShare.None, 16384, true))
                .AsTask();
        }

        private Task<HttpRouteResponse> HandleIcon(HttpListenerRequest request)
        {
            var stream = TryGetHostIconStream();
            return stream is null
                ? HttpRouteResponse.NotFound.AsTask()
                : HttpRouteResponse.Input(stream).AsTask();
        }

        private Task<HttpRouteResponse> HandleComplete(HttpListenerRequest request)
        {
            return HttpRouteResponse.Input(OpenOAuthCompleteHtml(), "text/html").AsTask();
        }
    }

    public class OAuthCompleteStatus
    {
        public bool success { get; set; }
        public string? username { get; set; }
        public string? message { get; set; }
        public string? stacktrace { get; set; }

        public static OAuthCompleteStatus Complete(string username)
        {
            return new OAuthCompleteStatus { success = true, username = username };
        }

        public static OAuthCompleteStatus Failed(string message, Exception? ex = null)
        {
            return new OAuthCompleteStatus { success = false, message = message, stacktrace = ex?.ToString() };
        }
    }

    public delegate OAuthCompleteStatus? OAuthComplete(bool success, IDictionary<string, string> parameters,
        string? content);

    public static object BackgroundPicChangeCallback(string pic)
    {
        lock (changeLock)
        {
            picAddress = pic;
            return true;
        }
    }

    public static bool StartOAuthWaitingCallback(string serviceName, string url, OAuthComplete completeCallback)
    {
        if (IsWebServerRunning(serviceName))
            return false;
        ModBase.RunInNewThread(() =>
        {
            var serverPort = 0;
            lock (_webServers)
            {
                string? currentPicAddress;
                lock (changeLock)
                {
                    currentPicAddress = picAddress;
                }

                var server = new NaidCallbackServer(serviceName, completeCallback, currentPicAddress);
                serverPort = server.Port;
                ModBase.Log($"[OAuth] {serviceName}: 已开始监听 {server.Port} 端口，正在初始化路由");
                var webServiceName = $"oauth/{serviceName}";
                if (DisposeWebServer(webServiceName)) ModBase.Log("[OAuth] 已关闭先前认证服务服务端");
                StartWebServer(webServiceName, server);
                ModBase.Log($"[OAuth] {serviceName}: 初始化完成，开始响应 HTTP 请求");
            }

            ModBase.OpenWebsite(url.Replace("%r", $"http://localhost:{serverPort}/callback"));
        }, $"CallbackWebServerLoading/{serviceName}");
        return true;
    }

    public static void StartNaidAuthorize(Action<bool>? completeCallback = null)
    {
        Exception? resultEx = null;
        StartOAuthWaitingCallback("NatayarkID",
            $"https://account.naids.com/oauth2/authorize?response_type=code&client_id={SecretsCompat.NatayarkClientId}&redirect_uri=%r",
            (success, parameters, content) =>
            {
                OAuthCompleteStatus? status;
                if (!success)
                {
                    ModMain.MyMsgBox(content ?? string.Empty, isWarn: true);
                    completeCallback?.Invoke(false);
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
                    status = OAuthCompleteStatus.Complete(NatayarkProfileManager.NaidProfile.Username);
                else
                    status = OAuthCompleteStatus.Failed("获取用户信息失败，请尝试重新登录", resultEx);
                completeCallback?.Invoke(status.success);
                return status;
            });
    }

    private static Stream OpenOAuthCompleteHtml()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream("PclNex.EasyTierLobby.Resources.oauth-complete.html")
               ?? new MemoryStream(Encoding.UTF8.GetBytes("<html><body>OAuth complete.</body></html>"));
    }

    private static Stream? TryGetHostIconStream()
    {
        try
        {
            return ModBase.GetResourceStream("Images/icon.ico");
        }
        catch
        {
            return null;
        }
    }
}

public static class SecretsCompat
{
    public static string NatayarkClientId => OriginalSecrets.NatayarkClientId;

    public static string[] LinkServers => OriginalSecrets.LinkServers;
}
