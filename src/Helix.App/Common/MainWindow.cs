#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
#endif
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Common;

/// <summary>
/// The two things the tray needs to do to the app window: put it away, and bring it
/// back.
/// </summary>
/// <remarks>
/// Windows-only in effect. Mac Catalyst exposes no public API for minimizing or hiding a
/// window scene — the AppKit route needs a private selector that would fail App Review —
/// and the Catalyst head has no tray to drive this from anyway, so both calls are no-ops
/// there.
///
/// Every call marshals to the UI thread itself. Callers are tray handlers and countdown
/// callbacks, none of which run on it, and reading <c>Application.Windows</c> off the UI
/// thread is not safe.
/// </remarks>
internal static class MainWindow
{
    /// <summary>
    /// Minimizes and then takes the window off the taskbar, leaving the tray icon as the
    /// way back. Falls back to a plain minimize where hiding is not available, so the
    /// window can never end up somewhere the user cannot reach it.
    /// </summary>
    public static void HideToTray()
    {
#if WINDOWS
        Dispatch(appWindow =>
        {
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }

            appWindow.Hide();
        });
#endif
    }

    /// <summary>Minimizes the window, leaving it on the taskbar.</summary>
    public static void Minimize()
    {
#if WINDOWS
        Dispatch(appWindow =>
        {
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        });
#endif
    }

    /// <summary>Shows, un-minimizes and focuses the window.</summary>
    public static void Restore()
    {
#if WINDOWS
        Dispatch(appWindow =>
        {
            appWindow.Show();

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            // Show() alone leaves the window behind whatever the user was looking at.
            appWindow.MoveInZOrderAtTop();
        });
#endif
    }

#if WINDOWS
    private static void Dispatch(Action<AppWindow> action)
    {
        // The checks live inside the dispatch, not before it: this is called from timer
        // and tray threads, where the window list read a moment earlier would describe a
        // different instant — and App.Current can be null on the way out of the process.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AppBase? app = AppBase.Current;
            if (app is null || app.Windows.Count == 0)
            {
                return;
            }

            object? nativeWindow = app.Windows[0].Handler?.PlatformView;
            if (nativeWindow is null)
            {
                return;
            }

            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);

            action(AppWindow.GetFromWindowId(windowId));
        });
    }
#endif
}
