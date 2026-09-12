using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Navigation;
using Helix.Application.Abstractions.Time;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Users.Commands;
using Microsoft.Extensions.Logging;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Services;

internal sealed class IdleLockService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IIdleTimeProvider _idleTime;
    private readonly ILogger<IdleLockService> _logger;

    private CancellationTokenSource? _cancellation;

    private string _returnRoute = PageNames.HomePage;

    public IdleLockService(IIdleTimeProvider idleTime, ILogger<IdleLockService> logger)
    {
        _idleTime = idleTime;
        _logger = logger;
    }

    public bool IsLocked { get; private set; }

    public void Start()
    {
        if (_cancellation is not null || !_idleTime.IsSupported)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        _ = RunAsync(_cancellation.Token);
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        IsLocked = false;
    }

    public Task LockNowAsync()
    {
        return IsLocked ? Task.CompletedTask : LockAsync(null);
    }

    public async Task UnlockAsync()
    {
        IsLocked = false;

        _logger.LogInformation("The session was unlocked.");

        await Shell.Current.GoToAsync($"//{_returnRoute}");

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(_returnRoute));
    }

    public async Task SignOutAsync()
    {
        IsLocked = false;

        Result result = await ScopedHandler.HandleAsync((LogoutUser h) => h.Handle());
        if (result.IsFailure)
        {
            _logger.LogWarning("Signing out from the lock screen failed: {Reason}", result.Error.Description);
        }

        App.ServiceProvider.GetRequiredService<DriveWatchdog>().Stop();
        App.ServiceProvider.GetRequiredService<MountReconciler>().Stop();
        App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();
        App.ServiceProvider.GetRequiredService<StorageAlertService>().Stop();

        Stop();

        ViewModels.BaseViewModel.ResetCountdown();
        Views.Drives.HomePage.ResetSessionState();

        await Shell.Current.GoToAsync($"//{PageNames.LoginPage}");

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(PageNames.LoginPage));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await CheckAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An idle check faulted; the next check will run as scheduled.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckAsync()
    {
        if (IsLocked)
        {
            return;
        }

        Result<SettingsModel> settings = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());
        if (settings.IsFailure)
        {
            return;
        }

        int minutes = settings.Value.IdleLockMinutes;
        if (minutes <= 0)
        {
            return;
        }

        if (_idleTime.GetIdleTime() < TimeSpan.FromMinutes(minutes))
        {
            return;
        }

        await LockAsync(minutes);
    }

    private async Task LockAsync(int? minutes)
    {
        IsLocked = true;

        if (minutes is int idleMinutes)
        {
            _logger.LogInformation("Locking the session after {Minutes} minutes without input.", idleMinutes);
        }
        else
        {
            _logger.LogInformation("Locking the session at the user's request.");
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                _returnRoute = CurrentRoute();

                await Shell.Current.GoToAsync($"//{PageNames.LockPage}");
            }
            catch (Exception ex)
            {
                IsLocked = false;

                _logger.LogError(ex, "Could not show the lock screen.");
            }
        });
    }

    private static string CurrentRoute()
    {
        string? route = Shell.Current?.CurrentState?.Location?.OriginalString?.TrimStart('/');

        return string.IsNullOrWhiteSpace(route) || route == PageNames.LockPage
            ? PageNames.HomePage
            : route;
    }
}
