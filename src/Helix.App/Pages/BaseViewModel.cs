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

    protected BaseViewModel()
    {
        _countdownService = App.ServiceProvider.GetRequiredService<ICountdownService>();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial bool TimerCancelled { get; set; }

    [ObservableProperty]
    public partial bool ShowRedoButton { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimerCount))]
    public partial int SecondsRemaining { get; set; }

    public string TimerCount => $"{SecondsRemaining} seconds";

    [RelayCommand]
    public async Task StartTimerAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());
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
        return Shell.Current.DisplayAlertAsync("Something went wrong!", error.Description, "Ok");
    }

    public static Task DisplaySuccessAsync(string message)
    {
        return Shell.Current.DisplayAlertAsync("Success!", message, "Ok");
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

    /// <summary>
    /// Wires up the countdown service events. Synchronous — safe to call from a ctor.
    /// Must be paired with <see cref="InitializeCountdownAsync"/> for the I/O half.
    /// </summary>
    public void InitializeCountdownEvents()
    {
        // The countdown timer raises these events on a thread-pool thread; the
        // properties are bound to UI, so marshal onto the main thread — WinUI throws
        // when PropertyChanged for a bound property fires off the UI thread.
        _countdownService.CountdownTick += (sender, remaining) =>
            MainThread.BeginInvokeOnMainThread(() => SecondsRemaining = remaining);

        _countdownService.CountdownFinished += (sender, args) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ShowRedoButton = true;
                TimerCancelled = true;
                MinimizeApp();
            });
    }

    /// <summary>
    /// Loads the current settings and starts the countdown if <c>AutoMinimize</c> is
    /// enabled. Awaited from page lifecycle methods — never blocks the UI thread.
    /// </summary>
    public async Task InitializeCountdownAsync(CancellationToken cancellationToken = default)
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync(
            (GetSettings h) => h.Handle(cancellationToken));
        if (result.IsFailure)
        {
            return;
        }

        SettingsModel settings = result.Value;
        if (settings.AutoMinimize)
        {
            _countdownService.Start(settings.TimerCount);
        }
    }
}
