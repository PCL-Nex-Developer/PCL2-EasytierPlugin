using PCL.Plugin.Abstractions;

namespace PclNex.EasyTierLobby.Services;

internal interface IPluginLog
{
    void Info(string message);
    void Warn(string message);
    void Error(Exception exception, string message);
}

internal sealed class PluginLogAdapter(IPluginLogger? logger) : IPluginLog
{
    public void Info(string message) => logger?.Info(message);

    public void Warn(string message) => logger?.Warn(message);

    public void Error(Exception exception, string message) => logger?.Error(message, exception);
}

internal sealed class NullPluginLog : IPluginLog
{
    public static NullPluginLog Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}
