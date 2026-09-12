using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.Application.Abstractions.Time;
using Helix.App.Services;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    private static bool _countdownStarted;
    private static int _countdownSeconds;
    private static bool _countdownDismissed;

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

    public string AppVersion => $"v{VersionInfo.Display}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCountdown))]
    public partial bool TimerCancelled { get; set; }

    [ObservableProperty]
    public partial bool ShowRedoButton { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountdownDisplay))]
    [NotifyPropertyChangedFor(nameof(ShowCountdown))]
    public partial int SecondsRemaining { get; set; }

    public string CountdownDisplay
    {
        get
        {
            TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(SecondsRemaining, 0));

            return $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
        }
    }

    public bool ShowCountdown => SecondsRemaining > 0 || TimerCancelled;

    [RelayCommand]
    public Task StartTimerAsync()
    {
        return StartCountdownAsync();
    }

    [RelayCommand]
    private async Task ResumeTimerAsync()
    {
        if (SecondsRemaining > 0)
        {
            _countdownDismissed = false;

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

        _countdownDismissed = true;
        TimerCancelled = true;
    }

    /// <summary>
    /// Holds the countdown where it is while the dashboard is not the page in front of the user.
    /// The seconds are kept, so returning to the dashboard picks up where it left off rather than
    /// minimizing the app out from under whatever page they walked away to.
    /// </summary>
    public void PauseCountdown()
    {
        if (!_countdownStarted)
        {
            return;
        }

        _countdownService.Stop();

        SecondsRemaining = _countdownService.SecondsRemaining;
    }

    private void ClearCountdown()
    {
        _countdownService.Reset();

        _countdownStarted = false;
        _countdownSeconds = 0;
        _countdownDismissed = false;

        SecondsRemaining = 0;
        ShowRedoButton = false;
        TimerCancelled = false;
    }

    public static void ResetCountdown()
    {
        App.ServiceProvider.GetRequiredService<ICountdownService>().Reset();

        _countdownStarted = false;
        _countdownSeconds = 0;
        _countdownDismissed = false;
    }

    public static Task DisplayErrorAsync(Error error)
    {
        Notifier.Error(error);

        return Task.CompletedTask;
    }

    public static Task DisplaySuccessAsync(string message)
    {
        Notifier.Success(message);

        return Task.CompletedTask;
    }

    public static void MinimizeApp()
    {
        TrayIconService tray = App.ServiceProvider.GetRequiredService<TrayIconService>();

        if (tray.IsRunning)
        {
            MainWindow.HideToTray();
            tray.NotifyHiddenToTray();

            return;
        }

        MainWindow.Minimize();
    }

    public void InitializeCountdownEvents()
    {
        if (_countdownEventsWired)
        {
            return;
        }

        _countdownEventsWired = true;

        _countdownService.CountdownTick += (sender, remaining) =>
            MainThread.BeginInvokeOnMainThread(() => SecondsRemaining = remaining);

        _countdownService.CountdownFinished += (sender, args) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _countdownDismissed = true;

                ShowRedoButton = true;
                TimerCancelled = true;
                MinimizeApp();
            });
    }

    /// <summary>
    /// Starts the countdown, or picks it back up where <see cref="PauseCountdown"/> left it.
    /// Settings are re-read every time, so auto-minimize switched off - or its timer changed -
    /// while the user was on another page is honoured the moment they come back.
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

        if (!settings.AutoMinimize || settings.TimerCount <= 0)
        {
            ClearCountdown();
            return;
        }

        if (!_countdownStarted || _countdownSeconds != settings.TimerCount)
        {
            StartCountdown(settings.TimerCount);
            return;
        }

        SecondsRemaining = _countdownService.SecondsRemaining;
        TimerCancelled = _countdownDismissed;

        if (_countdownDismissed || SecondsRemaining <= 0)
        {
            return;
        }

        _countdownService.Resume();
    }

    private async Task StartCountdownAsync(CancellationToken cancellationToken = default)
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync(
            (GetSettings h) => h.Handle(cancellationToken));
        if (result.IsFailure)
        {
            ClearCountdown();
            return;
        }

        SettingsModel settings = result.Value;
        if (!settings.AutoMinimize)
        {
            ClearCountdown();
            return;
        }

        StartCountdown(settings.TimerCount);
    }

    private void StartCountdown(int seconds)
    {
        if (seconds <= 0)
        {
            ClearCountdown();
            return;
        }

        _countdownStarted = true;
        _countdownSeconds = seconds;
        _countdownDismissed = false;

        _countdownService.Start(seconds);

        SecondsRemaining = seconds;
        ShowRedoButton = false;
        TimerCancelled = false;
    }
}
