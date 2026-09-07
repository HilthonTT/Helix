using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Services;
using Helix.App.ViewModels.Drives;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Auditlogs.Commands;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microcharts;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui;

namespace Helix.App.Views.Drives;

public sealed partial class HomePage : ContentPage
{
    private const string CreateDrive = "create-drive";
    private const string UpdateDrive = "update-drive";
    private const string DeleteDrive = "delete-drive";
    private const string DriveGroups = "drive-groups";

    private static bool _isFirstView = true;
    private static bool _prunedAuditlogs;
    private bool _isInitializing;

    private readonly INasConnector _nasConnector;
    private readonly HomeViewModel _viewModel;
    private readonly ModalHost _modals;
    private readonly DriveWatchdog _watchdog;
    private readonly TrayIconService _tray;
    private readonly StorageAlertService _storageAlerts;
    private readonly IdleLockService _idleLock;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = new HomeViewModel();

        BindingContext = _viewModel;

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _watchdog = App.ServiceProvider.GetRequiredService<DriveWatchdog>();
        _tray = App.ServiceProvider.GetRequiredService<TrayIconService>();
        _storageAlerts = App.ServiceProvider.GetRequiredService<StorageAlertService>();
        _idleLock = App.ServiceProvider.GetRequiredService<IdleLockService>();

        _modals = new ModalHost(BlockScreen);
        _modals.Register(CreateDrive, CreateDriveLayout, CreateDriveView);
        _modals.Register(UpdateDrive, UpdateDriveLayout, UpdateDriveView);
        _modals.Register(DeleteDrive, DeleteDriveLayout, DeleteDriveView);
        _modals.Register(DriveGroups, DriveGroupsLayout, DriveGroupsView);
        _modals.AttachEscapeToDismiss(this);

        DriveCard.SizeChanged += (_, _) => _viewModel.IsCompact = DriveCard.Width < CompactCardWidth;

        RegisterMessages();
    }

    private const double CompactCardWidth = 850;

    protected async override void OnAppearing()
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            List<Drive> drives = await _viewModel.FetchDrivesAsync();

            await InitializeChartAsync(drives);

            await _viewModel.FetchDriveGroupsAsync();

            await HandleConnectDrivesOnStartupAsync();

            await _viewModel.InitializeCountdownAsync();

            await _watchdog.StartAsync();

            await _tray.StartAsync();

            _storageAlerts.Start();

            _idleLock.Start();

            await PruneAuditlogsAsync();
        }
        catch (Exception ex)
        {
            Notifier.Error(ex.Message);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    internal static void ResetSessionState()
    {
        _isFirstView = true;
        _prunedAuditlogs = false;
    }

    private static async Task PruneAuditlogsAsync()
    {
        if (_prunedAuditlogs)
        {
            return;
        }

        _prunedAuditlogs = true;

        Result<int> result = await ScopedHandler.HandleAsync((PruneAuditlogs h) => h.Handle());
        if (result.IsFailure)
        {
            AppLog.For<HomePage>().LogWarning(
                "Could not prune the audit log: {Reason}",
                result.Error.Description);

            return;
        }

        if (result.Value > 0)
        {
            AppLog.For<HomePage>().LogInformation("Pruned {Count} expired audit entries.", result.Value);
        }
    }

    private async Task HandleConnectDrivesOnStartupAsync()
    {
        if (!_isFirstView)
        {
            return;
        }

        if (_viewModel.ConnectDrivesOnStartupCommand.CanExecute(null))
        {
            await _viewModel.ConnectDrivesOnStartupCommand.ExecuteAsync(null);

            _isFirstView = false;
        }
    }

    private async Task InitializeChartAsync(List<Drive>? providedDrives = null)
    {
        List<Drive> drives = providedDrives ?? await FetchDrivesFromDatabaseAsync();

        ChartEntry[] entries = GenerateChartEntries(drives);
        chart.Chart = CreateDonutChart(entries);
    }

    private static async Task<List<Drive>> FetchDrivesFromDatabaseAsync()
    {
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (result.IsFailure)
        {
            return [];
        }

        return result.Value;
    }

    private ChartEntry[] GenerateChartEntries(List<Drive> drives)
    {
        if (drives.Count == 0)
        {
            return BuildEntries(0, 1);
        }

        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        int connected = drives.Count(d => connectedLetters.Contains(d.Letter));

        return BuildEntries(connected, drives.Count - connected);
    }

    private static ChartEntry[] BuildEntries(int connected, int disconnected)
    {
        bool isLight = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Light;

        Color connectedColor = Color.FromArgb(isLight ? "#0E9F6E" : "#34D399");
        Color disconnectedColor = Color.FromArgb(isLight ? "#DC2626" : "#F87171");

        return
        [
            new ChartEntry(connected) { Color = connectedColor.ToSKColor() },
            new ChartEntry(disconnected) { Color = disconnectedColor.ToSKColor() }
        ];
    }

    private static DonutChart CreateDonutChart(ChartEntry[] entries)
    {
        return new DonutChart
        {
            Entries = entries,
            IsAnimated = true,
            HoleRadius = 0.68f,
            LabelTextSize = 24,
            BackgroundColor = Colors.Transparent.ToSKColor(),
        };
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CreateDriveMessage>(
            this, async (r, m) => await _modals.ToggleAsync(CreateDrive, m.Value));

        WeakReferenceMessenger.Default.Register<UpdateDriveMessage>(
            this, async (r, m) => await _modals.ToggleAsync(UpdateDrive, m.Value));

        WeakReferenceMessenger.Default.Register<DeleteDriveMessage>(
            this, async (r, m) => await _modals.ToggleAsync(DeleteDrive, m.Value));

        WeakReferenceMessenger.Default.Register<DriveGroupsMessage>(
            this, async (r, m) => await _modals.ToggleAsync(DriveGroups, m.Show));

        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(
            this, async (r, m) =>
            {
                try
                {
                    await InitializeChartAsync();
                }
                catch (Exception ex)
                {
                    AppLog.For<HomePage>().LogWarning(ex, "The drive chart could not be refreshed.");
                }
            });
    }

    private async void Preferences_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.GoToSettingsCommand.CanExecute(null))
        {
            await _viewModel.GoToSettingsCommand.ExecuteAsync(null);
        }
    }

    private void AddDrive_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.OpenCreateDriveModalCommand.CanExecute(null))
        {
            _viewModel.OpenCreateDriveModalCommand.Execute(null);
        }
    }

    private void Search_Clicked(object sender, EventArgs e) => DriveSearch.Focus();

    private void DriveGroups_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.OpenDriveGroupsModalCommand.CanExecute(null))
        {
            _viewModel.OpenDriveGroupsModalCommand.Execute(null);
        }
    }

    private void ExportDrives_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.ExportDrivesCommand.CanExecute(null))
        {
            _viewModel.ExportDrivesCommand.Execute(null);
        }
    }

    private void ImportDrives_Clicked(object sender, EventArgs e)
    {
        if (_viewModel.ImportDrivesCommand.CanExecute(null))
        {
            _viewModel.ImportDrivesCommand.Execute(null);
        }
    }
}
