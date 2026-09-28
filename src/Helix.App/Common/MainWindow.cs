#if WINDOWS
using Microsoft.UI;
using Microsoft.UI.Windowing;
#elif MACCATALYST
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
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
#elif MACCATALYST
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                IntPtr application = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (application != IntPtr.Zero)
                {
                    objc_msgSend(application, sel_registerName("activateIgnoringOtherApps:"), true);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                AppLog.For<App>().LogDebug(ex, "AppKit did not answer when bringing the window forward.");
            }
        });
#endif
    }

    public static bool IsExiting { get; private set; }

    public static volatile bool IsActive = true;

    public static void Exit()
    {
        IsExiting = true;

        MainThread.BeginInvokeOnMainThread(() => AppBase.Current?.Quit());
    }

#if MACCATALYST
    private const string ObjCRuntime = "/usr/lib/libobjc.dylib";

    [DllImport(ObjCRuntime)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCRuntime)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCRuntime)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime)]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
#endif

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
