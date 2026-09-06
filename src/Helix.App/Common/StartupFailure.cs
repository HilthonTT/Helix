using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Helix.App.Common;

/// <summary>
/// The last word when Helix cannot start: the database key could not be read, or the
/// database could not be opened or migrated.
/// </summary>
/// <remarks>
/// Both happen before there is a window, so nothing that normally reports a failure —
/// the banner, the tray — exists yet. Left alone, the exception either killed the
/// process silently or was swallowed by the WinUI last-resort handler and left a
/// windowless one behind; either way the user saw an app that did not open and had
/// nothing to say about it. This puts one native dialog up with the reason and where
/// the log is, writes the same to the log, and exits. On macOS there is no native
/// dialog reachable from here, so it logs and exits.
/// </remarks>
internal static class StartupFailure
{
    public static void Exit(ILogger logger, Exception exception)
    {
        logger.LogCritical(exception, "Helix cannot start.");

#if WINDOWS
        try
        {
            string text =
                $"{exception.Message}\n\n" +
                $"Nothing has been changed. The log is in the logs folder under:\n{FileSystem.AppDataDirectory}";

            _ = MessageBoxW(IntPtr.Zero, text, "Helix cannot start", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The start-up failure dialog could not be shown.");
        }
#endif

        Environment.Exit(1);
    }

#if WINDOWS
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
#endif
}
