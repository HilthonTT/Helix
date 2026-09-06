using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Settings;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Features.DriveGroups.Commands;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using SettingsModel = Helix.Domain.Settings.Settings;

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
    private const string GroupIdPrefix = "group:";

    private readonly ITrayIcon _trayIcon;
    private readonly INasConnector _nasConnector;
    private readonly IDriveMonitor _monitor;
    private readonly ILogger<TrayIconService> _logger;
    private readonly Lock _gate = new();

    /// <summary>The drives the menu was last built from, for turning an id into a name.</summary>
    private List<Drive> _drives = [];

    /// <summary>The groups the menu was last built from.</summary>
    private List<DriveGroup> _groups = [];

    private bool _subscribed;
    private bool _running;
    private bool _announcedHiding;

    /// <summary>How long, and how often, to keep asking the shell for the icon after it refused.</summary>
    private static readonly TimeSpan ShowRetryInterval = TimeSpan.FromSeconds(10);
    private const int ShowRetryAttempts = 18;

    private CancellationTokenSource? _showRetry;

    // The two settings that have to be answered without awaiting anything: the window's
    // Closing handler is a WinUI event that must decide before it returns, and the
    // notification goes up at the moment the window is put away. Read on the UI and tray
    // threads, written from the thread pool when the settings page reports a change —
    // volatile rather than locked because each is a single bool nothing else depends on.
    private volatile bool _closeToTray = SettingsModel.DefaultCloseToTray;
    private volatile bool _notifyOnMinimizeToTray = SettingsModel.DefaultNotifyOnMinimizeToTray;

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
    /// Whether closing the window should put Helix in the tray rather than quit it.
    /// </summary>
    /// <remarks>
    /// Answered from the cached setting, because the caller is the window's Closing
    /// handler and it has nowhere to await. Until the settings have been read — before
    /// sign-in, or where the read failed — this is the default, which is to keep
    /// running: hiding a window the user can get back beats quitting a session they
    /// meant to keep.
    /// </remarks>
    public bool ClosesToTray => _closeToTray;

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

            // Started at logon, Helix can be up before the taskbar is: the shell refuses
            // the icon, then adds it a few seconds later when TaskbarCreated arrives -
            // and this service had already recorded "no tray", leaving an icon with an
            // empty menu and a close button that quit. Asked again until it is there.
            _ = RetryShowAsync();

            return;
        }

        // This user's, not the last one's: the icon is put up again per sign-in.
        await LoadPreferencesAsync();

        await RefreshAsync();
    }

    private async Task RetryShowAsync()
    {
        _showRetry?.Cancel();

        var retry = new CancellationTokenSource();
        _showRetry = retry;

        try
        {
            for (int attempt = 0; attempt < ShowRetryAttempts; attempt++)
            {
                await Task.Delay(ShowRetryInterval, retry.Token);

                if (_running || !_trayIcon.Show(AppInfo.Current.Name))
                {
                    continue;
                }

                _running = true;

                _logger.LogInformation("The tray icon came up on attempt {Attempt}.", attempt + 1);

                await LoadPreferencesAsync();
                await RefreshAsync();

                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() ran: the session this icon was for is over.
        }
        finally
        {
            if (ReferenceEquals(_showRetry, retry))
            {
                _showRetry = null;
            }

            retry.Dispose();
        }
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

        _showRetry?.Cancel();

        // The next user gets the explanation too — they have not seen it.
        _announcedHiding = false;

        lock (_gate)
        {
            _running = false;
            _drives = [];
            _groups = [];
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
        if (!_running || _announcedHiding || !_notifyOnMinimizeToTray)
        {
            return;
        }

        _announcedHiding = true;

        _trayIcon.Notify(AppInfo.Current.Name, AppResources.TrayStillRunning);
    }

    /// <summary>
    /// Puts a notification up, if there is a tray to put it in.
    /// </summary>
    /// <remarks>
    /// The single way anything else in the app speaks to the user while the window is
    /// away, so every tray call stays behind this class and its <see cref="IsRunning"/>
    /// gate — a notification from an icon that was never shown goes nowhere, and on
    /// macOS there is no icon at all.
    /// </remarks>
    public void Notify(string title, string message)
    {
        if (!_running)
        {
            return;
        }

        _trayIcon.Notify(title, message);
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

        // A group that cannot be read is a group the menu goes without, rather than a
        // menu that fails to rebuild and leaves the last one on screen.
        Result<List<DriveGroup>> groupsResult = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());

        List<DriveGroup> groups = groupsResult.IsSuccess ? groupsResult.Value : [];

        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        // Re-checked after the awaits, and under the lock Stop() takes: a refresh started
        // just before sign-out would otherwise resume past Stop() and put the icon back
        // up, listing the previous user's drives over the sign-in page.
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _drives = drives;
            _groups = groups;

            _trayIcon.SetMenu(BuildMenu(drives, groups, connected));

            // Also re-checks that the icon is still there — Explorer can restart and
            // refuse it — so the window stops being hidable the moment the way back
            // disappears.
            _running = _trayIcon.Show($"{AppInfo.Current.Name} — {CountConnected(drives, connected)}/{drives.Count}");
        }
    }

    private static int CountConnected(List<Drive> drives, HashSet<string> connected) =>
        drives.Count(drive => connected.Contains(drive.Letter));

    private static List<TrayMenuItem> BuildMenu(
        List<Drive> drives,
        List<DriveGroup> groups,
        HashSet<string> connected)
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

        // Groups sit below the drives rather than above them: the tray's job is one
        // drive at a time first, and a list of groups at the top would push the thing
        // most people opened the menu for off the end of it.
        if (groups.Count > 0)
        {
            items.Add(TrayMenuItem.Separator);

            foreach (DriveGroup group in groups)
            {
                items.Add(new TrayMenuItem(
                    $"{GroupIdPrefix}{group.Id}",
                    string.Format(AppResources.TrayConnectGroup, group.Name)));
            }
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
                    MainWindow.Exit();
                    return;

                case ConnectAllId:
                    Report(await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle()));
                    break;

                case DisconnectAllId:
                    Report(await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle()));
                    break;

                default:
                    if (id.StartsWith(GroupIdPrefix, StringComparison.Ordinal))
                    {
                        await ConnectGroupAsync(id[GroupIdPrefix.Length..]);
                        break;
                    }

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

    /// <summary>
    /// Connects a whole group from the menu.
    /// </summary>
    /// <remarks>
    /// Connect only, with no disconnect twin: the drive entries above already toggle, and
    /// a menu carrying both directions for every group is a menu nobody reads. Taking a
    /// group down is a deliberate act that belongs on the dashboard.
    /// </remarks>
    private async Task ConnectGroupAsync(string rawGroupId)
    {
        if (!Guid.TryParse(rawGroupId, out Guid groupId))
        {
            return;
        }

        Report(await ScopedHandler.HandleAsync((ConnectDriveGroup h) => h.Handle(new ConnectDriveGroup.Request(groupId))));
    }

    /// <summary>
    /// Says what a menu action came back with, when it is not success.
    /// </summary>
    /// <remarks>
    /// The tray is used when the window is away, so there is no banner to land on, and
    /// the handlers write no log of their own: a "connect all" with a wrong password
    /// produced no toast, no log line and no audit row, and the menu simply rebuilt with
    /// the drive still marked disconnected. The balloon is what the tray has.
    /// </remarks>
    private void Report(Result result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        _logger.LogWarning("A tray action failed: {Reason}", result.Error.Description);

        _trayIcon.Notify(AppInfo.Current.Name, result.Error.Description);
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
            Report(await ScopedHandler.HandleAsync((DisconnectDrive h) => h.Handle(new DisconnectDrive.Request(driveId))));
            return;
        }

        Report(await ScopedHandler.HandleAsync((ConnectDrive h) => h.Handle(new ConnectDrive.Request(driveId))));
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
        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(this, (r, m) => RefreshSafely());

        // The menu does not change with the settings, but what the close button does and
        // whether the tray explains itself both do.
        WeakReferenceMessenger.Default.Register<SettingsChangedMessage>(this, (r, m) => ReloadPreferencesSafely());
    }

    /// <summary>
    /// Re-reads the two settings the tray has to answer without awaiting.
    /// </summary>
    /// <remarks>
    /// A failed read leaves the last known values in place rather than falling back to
    /// the defaults: the user turning close-to-tray off and then hitting a locked
    /// database should not quietly get it back.
    /// </remarks>
    private async Task LoadPreferencesAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        _closeToTray = result.Value.CloseToTray;
        _notifyOnMinimizeToTray = result.Value.NotifyOnMinimizeToTray;
    }

    /// <summary>
    /// Reloads without letting a failure escape as an unhandled exception — the same
    /// hazard <see cref="RefreshSafely"/> exists for.
    /// </summary>
    private void ReloadPreferencesSafely()
    {
        _ = LoadPreferencesAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The tray icon failed to re-read its settings."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
