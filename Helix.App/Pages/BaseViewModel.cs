using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SharedKernel;

namespace Helix.App.Pages;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    public static Task DisplayErrorAsync(Error error)
    {
        return Shell.Current.DisplayAlert("Something went wrong!", error.Description, "Ok");
    }

    public static Task DisplaySuccessAsync(string message)
    {
        return Shell.Current.DisplayAlert("Success!", message, "Ok");
    }

    public static void MinimizeApp()
    {
        if (App.Current?.Windows.Count <= 0 || App.Current?.Windows[0] is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Window? window = App.Current.Windows[0];

            object? nativeWindow = window.Handler?.PlatformView;

            if (nativeWindow is not null)
            {
                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                }
            }
        });
    }
}
