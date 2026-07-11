using System.IO;
using System.Text.Json;
using PCL.Core.App;
using PCL.Core.App.Localization;

namespace PCL.Core.App;

public enum LinkProtocolPreference
{
    Tcp,
    Udp
}

public static class LinkConfigCompat
{
    public static void TrySetValue(string? tag, object? value)
    {
        try
        {
            switch (tag)
            {
                case "LinkUsername":
                    Config.Link.Username = value?.ToString() ?? string.Empty;
                    break;
                case "LinkLatencyFirstMode":
                    Config.Link.UseLatencyFirstMode = ToBool(value);
                    break;
                case "LinkProtocolPreference":
                    Config.Link.ProtocolPreference = (LinkProtocolPreference)ToInt(value);
                    break;
                case "LinkTryPunchSym":
                    Config.Link.TryPunchSym = ToBool(value);
                    break;
                case "LinkEnableIPv6":
                    Config.Link.EnableIPv6 = ToBool(value);
                    break;
                case "LinkEnableCliOutput":
                    Config.Link.EnableCliOutput = ToBool(value);
                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Setup.GameLink.Error.ConfigChangeFailed"), ModBase.LogLevel.Hint);
        }
    }

    private static bool ToBool(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string stringValue => bool.TryParse(stringValue, out var parsed) && parsed,
            _ => false
        };
    }

    private static int ToInt(object? value)
    {
        return value switch
        {
            int intValue => intValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => 0
        };
    }
}

public static class Config
{
    public static LinkConfig Link { get; } = new();

    public sealed class LinkConfig
    {
        public event Action<string>? UsernameChanged;

        private string _username = string.Empty;
        private LinkRelayBehavior _relayType = LinkRelayBehavior.Default;
        private int _serverType = 1;
        private bool _useLatencyFirstMode = true;
        private string _customRelayServer = DefaultCustomRelayServer;
        private LinkProtocolPreference _protocolPreference = LinkProtocolPreference.Tcp;
        private bool _tryPunchSym = true;
        private bool _enableIPv6 = true;
        private bool _enableCliOutput;

        public string Username
        {
            get => _username;
            set
            {
                var username = value ?? string.Empty;
                if (string.Equals(_username, username, StringComparison.Ordinal))
                    return;

                _username = username;
                LinkPersistence.SaveConfig(this);
                UsernameChanged?.Invoke(username);
            }
        }

        public LinkRelayBehavior RelayType
        {
            get => _relayType;
            set => SetField(ref _relayType, value);
        }

        public int ServerType
        {
            get => _serverType;
            set => SetField(ref _serverType, value);
        }

        public bool UseLatencyFirstMode
        {
            get => _useLatencyFirstMode;
            set => SetField(ref _useLatencyFirstMode, value);
        }

        public string CustomRelayServer
        {
            get => _customRelayServer;
            set => SetField(ref _customRelayServer, value ?? string.Empty);
        }

        public LinkProtocolPreference ProtocolPreference
        {
            get => _protocolPreference;
            set => SetField(ref _protocolPreference, value);
        }

        public bool TryPunchSym
        {
            get => _tryPunchSym;
            set => SetField(ref _tryPunchSym, value);
        }

        public bool EnableIPv6
        {
            get => _enableIPv6;
            set => SetField(ref _enableIPv6, value);
        }

        public bool EnableCliOutput
        {
            get => _enableCliOutput;
            set => SetField(ref _enableCliOutput, value);
        }

        private static string DefaultCustomRelayServer => Environment.GetEnvironmentVariable("PCLNEX_EASYTIER_PEERS") ?? string.Empty;

        public void Reset()
        {
            Apply(new LinkConfigSnapshot(
                string.Empty,
                LinkRelayBehavior.Default,
                1,
                true,
                DefaultCustomRelayServer,
                LinkProtocolPreference.Tcp,
                true,
                true,
                false));
            LinkPersistence.SaveConfig(this);
        }

        internal void Apply(LinkConfigSnapshot snapshot)
        {
            _username = snapshot.Username ?? string.Empty;
            _relayType = snapshot.RelayType;
            _serverType = snapshot.ServerType;
            _useLatencyFirstMode = snapshot.UseLatencyFirstMode;
            _customRelayServer = snapshot.CustomRelayServer ?? DefaultCustomRelayServer;
            _protocolPreference = snapshot.ProtocolPreference;
            _tryPunchSym = snapshot.TryPunchSym;
            _enableIPv6 = snapshot.EnableIPv6;
            _enableCliOutput = snapshot.EnableCliOutput;
        }

        internal LinkConfigSnapshot ToSnapshot() => new(
            Username,
            RelayType,
            ServerType,
            UseLatencyFirstMode,
            CustomRelayServer,
            ProtocolPreference,
            TryPunchSym,
            EnableIPv6,
            EnableCliOutput);

        private void SetField<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            LinkPersistence.SaveConfig(this);
        }
    }
}

public static class States
{
    public static LinkStates Link { get; } = new();

    public sealed class LinkStates
    {
        private bool _linkEula;
        private string _naidRefreshToken = string.Empty;
        private string _naidRefreshExpireTime = string.Empty;
        private string _announceCache = string.Empty;
        private int _announceCacheVer;

        public LinkStates()
        {
            LinkEulaConfig = new BoolStateConfig(false, value => SetLinkEula(value, updateConfig: false));
            NaidRefreshTokenConfig = new StringStateConfig(string.Empty, value => SetNaidRefreshToken(value, updateConfig: false));
            NaidRefreshExpireTimeConfig = new StringStateConfig(string.Empty, value => SetNaidRefreshExpireTime(value, updateConfig: false));
            AnnounceCacheConfig = new StringStateConfig(string.Empty, value => SetAnnounceCache(value, updateConfig: false));
            AnnounceCacheVerConfig = new IntStateConfig(0, value => SetAnnounceCacheVer(value, updateConfig: false));
        }

        public BoolStateConfig LinkEulaConfig { get; }
        public StringStateConfig NaidRefreshTokenConfig { get; }
        public StringStateConfig NaidRefreshExpireTimeConfig { get; }
        public StringStateConfig AnnounceCacheConfig { get; }
        public IntStateConfig AnnounceCacheVerConfig { get; }

        public bool LinkEula
        {
            get => _linkEula;
            set => SetLinkEula(value, updateConfig: true);
        }

        public string NaidRefreshToken
        {
            get => _naidRefreshToken;
            set => SetNaidRefreshToken(value, updateConfig: true);
        }

        public string NaidRefreshExpireTime
        {
            get => _naidRefreshExpireTime;
            set => SetNaidRefreshExpireTime(value, updateConfig: true);
        }

        public string AnnounceCache
        {
            get => _announceCache;
            set => SetAnnounceCache(value, updateConfig: true);
        }

        public int AnnounceCacheVer
        {
            get => _announceCacheVer;
            set => SetAnnounceCacheVer(value, updateConfig: true);
        }

        internal void Apply(LinkStateSnapshot snapshot, string naidRefreshToken, string naidRefreshExpireTime)
        {
            SetLinkEula(snapshot.LinkEula, updateConfig: true, save: false);
            SetAnnounceCache(snapshot.AnnounceCache ?? string.Empty, updateConfig: true, save: false);
            SetAnnounceCacheVer(snapshot.AnnounceCacheVer, updateConfig: true, save: false);
            SetNaidRefreshToken(naidRefreshToken, updateConfig: true, save: false);
            SetNaidRefreshExpireTime(naidRefreshExpireTime, updateConfig: true, save: false);
        }

        internal LinkStateSnapshot ToSnapshot() => new(LinkEula, AnnounceCache, AnnounceCacheVer);

        private void SetLinkEula(bool value, bool updateConfig, bool save = true)
        {
            _linkEula = value;
            if (updateConfig) LinkEulaConfig.SetSilently(value);
            if (save) LinkPersistence.SaveState(this);
        }

        private void SetNaidRefreshToken(string? value, bool updateConfig, bool save = true)
        {
            _naidRefreshToken = value ?? string.Empty;
            if (updateConfig) NaidRefreshTokenConfig.SetSilently(_naidRefreshToken);
            if (save) LinkPersistence.SaveState(this);
        }

        private void SetNaidRefreshExpireTime(string? value, bool updateConfig, bool save = true)
        {
            _naidRefreshExpireTime = value ?? string.Empty;
            if (updateConfig) NaidRefreshExpireTimeConfig.SetSilently(_naidRefreshExpireTime);
            if (save) LinkPersistence.SaveState(this);
        }

        private void SetAnnounceCache(string? value, bool updateConfig, bool save = true)
        {
            _announceCache = value ?? string.Empty;
            if (updateConfig) AnnounceCacheConfig.SetSilently(_announceCache);
            if (save) LinkPersistence.SaveState(this);
        }

        private void SetAnnounceCacheVer(int value, bool updateConfig, bool save = true)
        {
            _announceCacheVer = value;
            if (updateConfig) AnnounceCacheVerConfig.SetSilently(value);
            if (save) LinkPersistence.SaveState(this);
        }
    }

    public abstract class StateConfig<T>(T defaultValue, Action<T>? changed = null)
    {
        private readonly T _defaultValue = defaultValue;
        private readonly Action<T>? _changed = changed;
        private T _value = defaultValue;

        public T Value
        {
            get => _value;
            set
            {
                _value = value;
                _changed?.Invoke(value);
            }
        }

        internal void SetSilently(T value) => _value = value;

        public void Reset() => Value = _defaultValue;
    }

    public sealed class BoolStateConfig(bool defaultValue = false, Action<bool>? changed = null) : StateConfig<bool>(defaultValue, changed);

    public sealed class IntStateConfig(int defaultValue = 0, Action<int>? changed = null) : StateConfig<int>(defaultValue, changed);

    public sealed class StringStateConfig(string defaultValue = "", Action<string>? changed = null) : StateConfig<string>(defaultValue, changed);
}

internal sealed record LinkConfigSnapshot(
    string Username,
    LinkRelayBehavior RelayType,
    int ServerType,
    bool UseLatencyFirstMode,
    string CustomRelayServer,
    LinkProtocolPreference ProtocolPreference,
    bool TryPunchSym,
    bool EnableIPv6,
    bool EnableCliOutput);

internal sealed record LinkStateSnapshot(bool LinkEula, string AnnounceCache, int AnnounceCacheVer);

internal static class LinkPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string? _dataDirectory;
    private static bool _isLoading;

    private static string ConfigFile => Path.Combine(_dataDirectory!, "link-config.json");
    private static string StateFile => Path.Combine(_dataDirectory!, "link-state.json");
    private static string NaidTokenFile => Path.Combine(_dataDirectory!, "natayark-refresh-token.txt");
    private static string NaidExpireFile => Path.Combine(_dataDirectory!, "natayark-refresh-expire.txt");

    public static void Initialize(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        Directory.CreateDirectory(dataDirectory);

        _isLoading = true;
        try
        {
            LoadConfig();
            LoadState();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public static void SaveConfig(Config.LinkConfig config)
    {
        if (!CanSave()) return;
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config.ToSnapshot(), JsonOptions));
    }

    public static void SaveState(States.LinkStates state)
    {
        if (!CanSave()) return;
        File.WriteAllText(StateFile, JsonSerializer.Serialize(state.ToSnapshot(), JsonOptions));
        WriteOrDelete(NaidTokenFile, state.NaidRefreshToken);
        WriteOrDelete(NaidExpireFile, state.NaidRefreshExpireTime);
    }

    private static void LoadConfig()
    {
        if (!File.Exists(ConfigFile)) return;
        var snapshot = JsonSerializer.Deserialize<LinkConfigSnapshot>(File.ReadAllText(ConfigFile), JsonOptions);
        if (snapshot is not null)
        {
            Config.Link.Apply(snapshot);
        }
    }

    private static void LoadState()
    {
        var snapshot = File.Exists(StateFile)
            ? JsonSerializer.Deserialize<LinkStateSnapshot>(File.ReadAllText(StateFile), JsonOptions)
            : new LinkStateSnapshot(true, string.Empty, 0);
        snapshot ??= new LinkStateSnapshot(true, string.Empty, 0);

        States.Link.Apply(
            snapshot,
            ReadTrimmed(NaidTokenFile),
            ReadTrimmed(NaidExpireFile));
    }

    private static bool CanSave() => !_isLoading && !string.IsNullOrWhiteSpace(_dataDirectory);

    private static string ReadTrimmed(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;

    private static void WriteOrDelete(string path, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        File.WriteAllText(path, value);
    }
}