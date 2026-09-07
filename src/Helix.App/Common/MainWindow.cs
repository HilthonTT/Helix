#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
#endif
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Common;

internal static class MainWindow
{
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

            appWindow.MoveInZOrderAtTop();
        });
#endif
    }

    public static bool IsExiting { get; private set; }

    public static void Exit()
    {
        IsExiting = true;

        MainThread.BeginInvokeOnMainThread(() => AppBase.Current?.Quit());
    }

#if WINDOWS
    private static void Dispatch(Action<AppWindow> action)
    {
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
