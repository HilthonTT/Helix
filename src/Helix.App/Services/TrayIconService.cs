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

    private List<Drive> _drives = [];

    private List<DriveGroup> _groups = [];

    private bool _subscribed;
    private bool _running;
    private bool _announcedHiding;

    private static readonly TimeSpan ShowRetryInterval = TimeSpan.FromSeconds(10);
    private const int ShowRetryAttempts = 18;

    private CancellationTokenSource? _showRetry;

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

    public bool IsSupported => _trayIcon.IsSupported;

    public bool IsRunning => _running;

    public bool ClosesToTray => _closeToTray;

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

        _running = _trayIcon.Show(AppInfo.Current.Name);

        if (!_running)
        {
            _logger.LogWarning("The tray icon is unavailable; the window will minimize to the taskbar instead.");

            _ = RetryShowAsync();

            return;
        }

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

    public void Stop()
    {
        if (!_trayIcon.IsSupported)
        {
            return;
        }

        _showRetry?.Cancel();

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

    public void NotifyHiddenToTray()
    {
        if (!_running || _announcedHiding || !_notifyOnMinimizeToTray)
        {
            return;
        }

        _announcedHiding = true;

        _trayIcon.Notify(AppInfo.Current.Name, AppResources.TrayStillRunning);
    }

    public void Notify(string title, string message)
    {
        if (!_running)
        {
            return;
        }

        _trayIcon.Notify(title, message);
    }

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

        Result<List<DriveGroup>> groupsResult = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());

        List<DriveGroup> groups = groupsResult.IsSuccess ? groupsResult.Value : [];

        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _drives = drives;
            _groups = groups;

            _trayIcon.SetMenu(BuildMenu(drives, groups, connected));

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

            await _monitor.PollAsync();

            PublishToUi();

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The tray icon failed to handle the menu selection {MenuItemId}.", id);
        }
    }

    private async Task ConnectGroupAsync(string rawGroupId)
    {
        if (!Guid.TryParse(rawGroupId, out Guid groupId))
        {
            return;
        }

        Report(await ScopedHandler.HandleAsync((ConnectDriveGroup h) => h.Handle(new ConnectDriveGroup.Request(groupId))));
    }

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
        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) => RefreshSafely());
        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) => RefreshSafely());
        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) => RefreshSafely());
        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(this, (r, m) => RefreshSafely());

        WeakReferenceMessenger.Default.Register<SettingsChangedMessage>(this, (r, m) => ReloadPreferencesSafely());
    }

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

    private void ReloadPreferencesSafely()
    {
        _ = LoadPreferencesAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The tray icon failed to re-read its settings."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RefreshSafely()
    {
        _ = RefreshAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The tray icon failed to refresh its menu."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
