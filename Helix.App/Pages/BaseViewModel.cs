using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.Application.Abstractions.Time;
using Helix.Application.Settings;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SharedKernel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Pages;

public abstract partial class BaseViewModel : ObservableObject
{
    private readonly ICountdownService _countdownService;
    private readonly GetSettings _getSettings;

    protected BaseViewModel()
    {
        _getSettings = App.ServiceProvider.GetRequiredService<GetSettings>();
        _countdownService = App.ServiceProvider.GetRequiredService<ICountdownService>();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool _timerCancelled;

    [ObservableProperty]
    private bool _showRedoButton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimerCount))]
    private int _secondsRemaining = 0;

    public string TimerCount => $"{SecondsRemaining} seconds";


    [RelayCommand]
    public async Task StartTimerAsync()
    {
        Result<SettingsModel> result = await _getSettings.Handle();
        if (result.IsSuccess)
        {
            SettingsModel settings = result.Value;
            if (settings.AutoMinimize)
            {
                _countdownService.Start(settings.TimerCount);

                ShowRedoButton = false;
            }
        }
    }

    [RelayCommand]
    private void ResumeTimer()
    {
        _countdownService.Resume();
        TimerCancelled = false;
    }

    [RelayCommand]
    private void CancelTimer()
    {
        _countdownService.Stop();

        TimerCancelled = true;
    }

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

    public void InitializeCountdownEvents()
    {
        Task.Run(async () =>
        {
            Result<SettingsModel> result = await _getSettings.Handle();
            if (result.IsSuccess)
            {
                SettingsModel settings = result.Value;
                if (settings.AutoMinimize)
                {
                    _countdownService.Start(settings.TimerCount);
                }
            }
        });

        _countdownService.CountdownTick += (sender, remaining) =>
        {
            // Update the view model property for binding
            SecondsRemaining = remaining;
        };

        _countdownService.CountdownFinished += (sender, args) =>
        {
            // Perform action when timer reaches 0
            ShowRedoButton = true;
            TimerCancelled = true;
            MinimizeApp();
        };
    }
}
