using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.Application.Abstractions.Time;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using AppBase = Microsoft.Maui.Controls.Application;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    /// <summary>
    /// Whether the auto-minimize countdown has already been armed this session.
    /// </summary>
    /// <remarks>
    /// The countdown is a once-per-sign-in affair, not a once-per-page-visit one. Shell
    /// caches the dashboard and raises <c>OnAppearing</c> every time the user navigates
    /// back to it, so without this flag the countdown was re-armed — and the window
    /// minimized again — on each return. Static because the countdown service behind it
    /// is a singleton that outlives any single viewmodel; <see cref="ResetCountdown"/>
    /// clears it on sign-out.
    /// </remarks>
    private static bool _countdownStarted;

    private readonly ICountdownService _countdownService;

    private bool _countdownEventsWired;

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
    public Task StartTimerAsync()
    {
        return StartCountdownAsync();
    }

    [RelayCommand]
    private async Task ResumeTimerAsync()
    {
        // Resume only picks up a countdown that still has time left on it. Once it has
        // run out there is nothing to resume, so the chip has to arm a fresh one —
        // otherwise it looked live but did nothing after the app had minimized once.
        if (SecondsRemaining > 0)
        {
            _countdownService.Resume();
            TimerCancelled = false;

            return;
        }

        await StartCountdownAsync();
    }

    [RelayCommand]
    private void CancelTimer()
    {
        _countdownService.Stop();

        TimerCancelled = true;
    }

    /// <summary>
    /// Stops the countdown and re-arms it for the next sign-in. The countdown service is
    /// a singleton that outlives the session: left running, it would minimize the window
    /// while the login page is on screen, and the next user would inherit its remaining
    /// seconds instead of their own setting.
    /// </summary>
    public static void ResetCountdown()
    {
        App.ServiceProvider.GetRequiredService<ICountdownService>().Reset();

        _countdownStarted = false;
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
        // The window list is only safe to read on the UI thread, and this is called from
        // a timer callback — so the checks happen inside the dispatch, not before it,
        // where they would have described a different moment (and dereferenced a null
        // App.Current on the way out of the process).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AppBase? app = App.Current;
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
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        });
    }

    /// <summary>
    /// Wires up the countdown service events. Synchronous — safe to call from a ctor.
    /// Must be paired with <see cref="InitializeCountdownAsync"/> for the I/O half.
    /// </summary>
    public void InitializeCountdownEvents()
    {
        // The service is a singleton, so a second subscription would never be collected
        // and every tick would run this viewmodel's handlers twice.
        if (_countdownEventsWired)
        {
            return;
        }

        _countdownEventsWired = true;

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
    /// Starts the countdown once per sign-in if <c>AutoMinimize</c> is enabled. Awaited
    /// from page lifecycle methods, which fire again on every return to the page — the
    /// repeat calls are deliberately no-ops.
    /// </summary>
    public Task InitializeCountdownAsync(CancellationToken cancellationToken = default)
    {
        if (_countdownStarted)
        {
            return Task.CompletedTask;
        }

        return StartCountdownAsync(cancellationToken);
    }

    private async Task StartCountdownAsync(CancellationToken cancellationToken = default)
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync(
            (GetSettings h) => h.Handle(cancellationToken));
        if (result.IsFailure)
        {
            return;
        }

        SettingsModel settings = result.Value;
        if (!settings.AutoMinimize)
        {
            // Nothing was armed, so nothing has been used up: leaving the flag clear
            // lets the countdown start if the setting is switched on later this session.
            return;
        }

        _countdownStarted = true;

        _countdownService.Start(settings.TimerCount);

        SecondsRemaining = settings.TimerCount;
        ShowRedoButton = false;
        TimerCancelled = false;
    }
}
