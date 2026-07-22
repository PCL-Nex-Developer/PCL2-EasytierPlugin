using System.Runtime.InteropServices;
using System.Windows;
using PCL.Core.Logging;

namespace PCL;

internal static class CeClipboard
{
    private const int RetryCount = 6;

    public static bool SetText(string? text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => SetText(text));

        Exception? lastException = null;
        for (var attempt = 0; attempt < RetryCount; attempt++)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                return true;
            }
            catch (COMException ex)
            {
                lastException = ex;
            }
            catch (ExternalException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(25 * (attempt + 1));
        }

        if (lastException is not null)
            LogWrapper.Debug(lastException, "EasyTier", "Clipboard is busy; copy request was ignored.");
        return false;
    }
}
