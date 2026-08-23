using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Navigation;
using Helix.Application.Abstractions.Time;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Users.Commands;
using Microsoft.Extensions.Logging;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Services;

/// <summary>
/// Watches how long the machine has been left alone and puts the lock screen up.
/// </summary>
/// <remarks>
/// A lock, not a sign-out, and the distinction is the whole design: the session stays
/// live, the drives stay mounted, and <see cref="DriveWatchdog"/> and
/// <see cref="TrayIconService"/> keep running behind the lock screen. An unattended NAS
/// tool that stopped reconnecting the moment nobody was at the keyboard would have it
/// exactly backwards — that is the point at which it matters most.
///
/// What locking protects is the machine somebody else can walk up to. Everything on the
/// other side of it — the drive list, the credentials, the audit log — is one click away
/// while the app is on screen, and there is no other gate once the user has signed in.
/// </remarks>
internal sealed class IdleLockService
{
    /// <summary>
    /// How often the idle time is read.
    /// </summary>
    /// <remarks>
    /// Thirty seconds, which is also the worst case by which a lock is late. Reading the
    /// idle time is a single system call, but locking is measured in minutes and polling
    /// faster would buy precision nobody asked for.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IIdleTimeProvider _idleTime;
    private readonly ILogger<IdleLockService> _logger;

    private CancellationTokenSource? _cancellation;

    /// <summary>The route to come back to, captured when the lock went up.</summary>
    private string _returnRoute = PageNames.HomePage;

    public IdleLockService(IIdleTimeProvider idleTime, ILogger<IdleLockService> logger)
    {
        _idleTime = idleTime;
        _logger = logger;
    }

    /// <summary>Whether the lock screen is currently up.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// Begins watching. Safe to call on every dashboard appearance, like the services it
    /// sits beside.
    /// </summary>
    public void Start()
    {
        if (_cancellation is not null || !_idleTime.IsSupported)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        _ = RunAsync(_cancellation.Token);
    }

    /// <summary>Stops watching. Called on sign-out — there is nothing left to lock.</summary>
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
                // Already torn down by a concurrent Stop.
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        IsLocked = false;
    }

    /// <summary>
    /// Puts the user back where they were, once the password has been accepted.
    /// </summary>
    /// <remarks>
    /// The route is the one they were on when the lock went up rather than always the
    /// dashboard: someone reading the audit log who went to make tea should come back to
    /// the audit log.
    /// </remarks>
    public async Task UnlockAsync()
    {
        IsLocked = false;

        _logger.LogInformation("The session was unlocked.");

        await Shell.Current.GoToAsync($"//{_returnRoute}");

        WeakReferenceMessenger.Default.Send(new PageChangedMessage(_returnRoute));
    }

    /// <summary>
    /// Ends the session from the lock screen, for the user who is finished rather than
    /// away.
    /// </summary>
    public async Task SignOutAsync()
    {
        IsLocked = false;

        Result result = await ScopedHandler.HandleAsync((LogoutUser h) => h.Handle());
        if (result.IsFailure)
        {
            _logger.LogWarning("Signing out from the lock screen failed: {Reason}", result.Error.Description);
        }

        // The same teardown the sidebar's sign-out does: every background service acts as
        // the signed-in user, so none of them may outlive the session.
        App.ServiceProvider.GetRequiredService<DriveWatchdog>().Stop();
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
                await CheckAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The idle watch faulted; the session will not lock itself this session.");
        }
    }

    private async Task CheckAsync()
    {
        if (IsLocked)
        {
            return;
        }

        // Re-read every tick rather than cached at start: the setting is changed on the
        // page next door, and a lock timer that only takes effect at the next sign-in is
        // a setting that looks broken.
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

    private async Task LockAsync(int minutes)
    {
        IsLocked = true;

        _logger.LogInformation("Locking the session after {Minutes} minutes without input.", minutes);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                // Captured before navigating, so unlocking returns to whatever page was
                // left open rather than always to the dashboard.
                _returnRoute = CurrentRoute();

                await Shell.Current.GoToAsync($"//{PageNames.LockPage}");
            }
            catch (Exception ex)
            {
                // A failed navigation must not leave IsLocked stuck true, or the session
                // would never lock again and never unlock either.
                IsLocked = false;

                _logger.LogError(ex, "Could not show the lock screen.");
            }
        });
    }

    /// <summary>
    /// The route currently on screen, or the dashboard when it cannot be read.
    /// </summary>
    private static string CurrentRoute()
    {
        string? route = Shell.Current?.CurrentState?.Location?.OriginalString?.TrimStart('/');

        return string.IsNullOrWhiteSpace(route) || route == PageNames.LockPage
            ? PageNames.HomePage
            : route;
    }
}
