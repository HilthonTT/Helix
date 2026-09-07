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

    public string CountdownDisplay => TimeSpan.FromSeconds(Math.Max(SecondsRemaining, 0)).ToString(@"m\:ss");

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

    private void ClearCountdown()
    {
        SecondsRemaining = 0;
        ShowRedoButton = false;
        TimerCancelled = false;
    }

    public static void ResetCountdown()
    {
        App.ServiceProvider.GetRequiredService<ICountdownService>().Reset();

        _countdownStarted = false;
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
                ShowRedoButton = true;
                TimerCancelled = true;
                MinimizeApp();
            });
    }

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
            ClearCountdown();
            return;
        }

        SettingsModel settings = result.Value;
        if (!settings.AutoMinimize)
        {
            ClearCountdown();
            return;
        }

        _countdownStarted = true;

        _countdownService.Start(settings.TimerCount);

        SecondsRemaining = settings.TimerCount;
        ShowRedoButton = false;
        TimerCancelled = false;
    }
}
