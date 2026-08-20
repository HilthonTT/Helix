using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

/// <summary>
/// Gives the tray icon its menu, and turns what the user clicks there into use cases.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="DriveWatchdog"/>: the watchdog reacts to what the
/// drives do, this reacts to what the user does about them. Both live here rather than
/// in Infrastructure because both need a DI scope per operation, and the tray icon
/// itself is a singleton that must never capture one.
///
/// It is also where the app becomes usable while its window is away. Auto-minimize used
/// to send Helix to the taskbar and leave the user with nothing to click; with the tray
/// running, the window can be put away properly and every drive is still one click from
/// connecting.
/// </remarks>
internal sealed class TrayIconService
{
    private const string OpenId = "open";
    private const string ConnectAllId = "connect-all";
    private const string DisconnectAllId = "disconnect-all";
    private const string ExitId = "exit";
    private const string DriveIdPrefix = "drive:";

    private readonly ITrayIcon _trayIcon;
    private readonly INasConnector _nasConnector;
    private readonly IDriveMonitor _monitor;
    private readonly ILogger<TrayIconService> _logger;
    private readonly Lock _gate = new();

    /// <summary>The drives the menu was last built from, for turning an id into a name.</summary>
    private List<Drive> _drives = [];

    private bool _subscribed;
    private bool _running;
    private bool _announcedHiding;

    public TrayIconService(
        ITrayIcon trayIcon,
        INasConnector nasConnector,
        IDriveMonitor monitor,
        ILogger<TrayIconService> logger)
    {
        _trayIcon = trayIcon;
        _nasConnector = nasConnector;
        _monitor = monitor;
        _logger = logger;
    }

    /// <summary>Whether the running platform has a tray at all.</summary>
    public bool IsSupported => _trayIcon.IsSupported;

    /// <summary>Whether the icon is currently showing — false before sign-in and after sign-out.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Puts the icon up and fills in its menu. Safe to call on every dashboard
    /// appearance, like the watchdog it sits beside.
    /// </summary>
    public async Task StartAsync()
    {
        if (!_trayIcon.IsSupported)
        {
            return;
        }

        if (!_subscribed)
        {
            _trayIcon.Activated += OnActivated;
            _trayIcon.MenuItemSelected += OnMenuItemSelected;
            _monitor.ConnectivityChanged += OnConnectivityChanged;

            RegisterMessages();

            _subscribed = true;
        }

        // Driven by whether the icon actually appeared, never assumed. IsRunning is what
        // permits the window to be hidden, and hiding it when there is no icon leaves the
        // user with no way back short of the task manager.
        _running = _trayIcon.Show(AppInfo.Current.Name);

        if (!_running)
        {
            _logger.LogWarning("The tray icon is unavailable; the window will minimize to the taskbar instead.");

            return;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Takes the icon down. Called on sign-out: its menu lists one user's drives and its
    /// commands run as that user, so it must not outlive the session.
    /// </summary>
    public void Stop()
    {
        if (!_trayIcon.IsSupported)
        {
            return;
        }

        _running = false;

        // The next user gets the explanation too — they have not seen it.
        _announcedHiding = false;

        lock (_gate)
        {
            _drives = [];
        }

        _trayIcon.SetMenu([]);
        _trayIcon.Hide();
    }

    /// <summary>
    /// Says where the window went, the first time it is put away in a session.
    /// </summary>
    /// <remarks>
    /// A window that disappears from the taskbar with no explanation reads as a crash.
    /// Once is enough — after that the user knows, and a notification on every timeout
    /// would be the most annoying thing in the app.
    /// </remarks>
    public void NotifyHiddenToTray()
    {
        if (!_running || _announcedHiding)
        {
            return;
        }

        _announcedHiding = true;

        _trayIcon.Notify(AppInfo.Current.Name, AppResources.TrayStillRunning);
    }

    /// <summary>
    /// Re-reads the drives, rebuilds the menu and refreshes the tooltip.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (!_running)
        {
            return;
        }

        Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        List<Drive> drives = result.Value;

        lock (_gate)
        {
            _drives = drives;
        }

        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        _trayIcon.SetMenu(BuildMenu(drives, connected));

        // Also re-checks that the icon is still there — Explorer can restart and refuse
        // it — so the window stops being hidable the moment the way back disappears.
        _running = _trayIcon.Show($"{AppInfo.Current.Name} — {CountConnected(drives, connected)}/{drives.Count}");
    }

    private static int CountConnected(List<Drive> drives, HashSet<string> connected) =>
        drives.Count(drive => connected.Contains(drive.Letter));

    private static List<TrayMenuItem> BuildMenu(List<Drive> drives, HashSet<string> connected)
    {
        List<TrayMenuItem> items =
        [
            new(OpenId, AppResources.TrayOpen),
        ];

        if (drives.Count > 0)
        {
            items.Add(TrayMenuItem.Separator);

            // One entry per drive, labelled with the action it will perform rather than
            // the state it is in — a menu is a list of things to do, and "Connect Z:"
            // cannot be misread the way a checkmark beside "Z:" can.
            foreach (Drive drive in drives.OrderBy(d => d.Letter, StringComparer.OrdinalIgnoreCase))
            {
                bool isConnected = connected.Contains(drive.Letter);

                string verb = isConnected ? AppResources.Disconnect : AppResources.Connect;

                items.Add(new TrayMenuItem($"{DriveIdPrefix}{drive.Id}", $"{verb} {drive.Letter}: — {drive.Name}"));
            }

            items.Add(TrayMenuItem.Separator);
            items.Add(new TrayMenuItem(ConnectAllId, AppResources.TrayConnectAll));
            items.Add(new TrayMenuItem(DisconnectAllId, AppResources.TrayDisconnectAll));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem(ExitId, AppResources.TrayExit));

        return items;
    }

    private void OnActivated(object? sender, EventArgs e) => MainWindow.Restore();

    private void OnMenuItemSelected(object? sender, string id) => _ = HandleSelectionAsync(id);

    private async Task HandleSelectionAsync(string id)
    {
        try
        {
            switch (id)
            {
                case OpenId:
                    MainWindow.Restore();
                    return;

                case ExitId:
                    // Hide first: an icon whose process has gone stays in the tray until
                    // the user happens to mouse over it.
                    Stop();
                    MainThread.BeginInvokeOnMainThread(() => Microsoft.Maui.Controls.Application.Current?.Quit());
                    return;

                case ConnectAllId:
                    await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle());
                    break;

                case DisconnectAllId:
                    await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle());
                    break;

                default:
                    if (!id.StartsWith(DriveIdPrefix, StringComparison.Ordinal))
                    {
                        return;
                    }

                    await ToggleDriveAsync(id[DriveIdPrefix.Length..]);
                    break;
            }

            // The dashboard may be on screen behind the menu; keep it in step.
            await _monitor.PollAsync();

            PublishToUi();

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            // Raised from a tray thread with nowhere to report to — a dialog behind a
            // hidden window would be worse than the failure itself.
            _logger.LogError(ex, "The tray icon failed to handle the menu selection {MenuItemId}.", id);
        }
    }

    private async Task ToggleDriveAsync(string rawDriveId)
    {
        if (!Guid.TryParse(rawDriveId, out Guid driveId))
        {
            return;
        }

        // Read connectivity now rather than trusting the label: the menu may have been
        // built minutes ago and the drive could have dropped since.
        Drive? drive;
        lock (_gate)
        {
            drive = _drives.FirstOrDefault(d => d.Id == driveId);
        }

        if (drive is null)
        {
            return;
        }

        if (_nasConnector.IsConnected(drive.Letter))
        {
            await ScopedHandler.HandleAsync((DisconnectDrive h) => h.Handle(new DisconnectDrive.Request(driveId)));
            return;
        }

        await ScopedHandler.HandleAsync((ConnectDrive h) => h.Handle(new ConnectDrive.Request(driveId)));
    }

    /// <summary>
    /// Reports a drive changing state, which is the whole reason the app is allowed to
    /// disappear into the tray: something happened while nobody was looking, and this is
    /// where the user finds out.
    /// </summary>
    private void OnConnectivityChanged(object? sender, IReadOnlyList<DriveConnectivityChange> changes)
    {
        if (!_running)
        {
            return;
        }

        foreach (DriveConnectivityChange change in changes)
        {
            string name = NameFor(change.DriveId) ?? change.Letter;

            string title = change.IsConnected
                ? AppResources.TrayDriveReconnected
                : AppResources.TrayDriveDisconnected;

            _trayIcon.Notify(title, $"{name} ({change.Letter}:)");
        }

        // The menu's verbs are now wrong for whatever just changed.
        _ = RefreshAsync();
    }

    private string? NameFor(Guid driveId)
    {
        lock (_gate)
        {
            return _drives.FirstOrDefault(d => d.Id == driveId)?.Name;
        }
    }

    private static void PublishToUi()
    {
        MainThread.BeginInvokeOnMainThread(() =>
            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage()));
    }

    private void RegisterMessages()
    {
        // The menu is only as good as the drive list it was built from.
        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) => RefreshSafely());
        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) => RefreshSafely());
        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) => RefreshSafely());
    }

    /// <summary>
    /// Refreshes without letting a failure escape as an unhandled exception.
    /// </summary>
    /// <remarks>
    /// The messenger's handler delegate returns void, so an <c>async</c> lambda here is
    /// an async void: anything thrown after the first await is rethrown on the thread
    /// pool, where <c>TaskScheduler.UnobservedTaskException</c> does not reach it and the
    /// process goes down. A locked database during a refresh is enough to trigger it, so
    /// the continuation swallows and logs instead.
    /// </remarks>
    private void RefreshSafely()
    {
        _ = RefreshAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The tray icon failed to refresh its menu."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
