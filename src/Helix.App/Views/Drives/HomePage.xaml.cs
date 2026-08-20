using CommunityToolkit.Mvvm.Messaging;
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
    private const string SearchDrives = "search-drives";

    private static bool _isFirstView = true;
    private static bool _prunedAuditlogs;
    private bool _isInitializing;

    private readonly INasConnector _nasConnector;
    private readonly HomeViewModel _viewModel;
    private readonly ModalHost _modals;
    private readonly DriveWatchdog _watchdog;
    private readonly TrayIconService _tray;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = new HomeViewModel();

        BindingContext = _viewModel;

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _watchdog = App.ServiceProvider.GetRequiredService<DriveWatchdog>();
        _tray = App.ServiceProvider.GetRequiredService<TrayIconService>();

        _modals = new ModalHost(BlockScreen);
        _modals.Register(CreateDrive, CreateDriveLayout, CreateDriveView);
        _modals.Register(UpdateDrive, UpdateDriveLayout, UpdateDriveView);
        _modals.Register(DeleteDrive, DeleteDriveLayout, DeleteDriveView);
        _modals.Register(SearchDrives, SearchDrivesLayout, SearchDrivesView);
        _modals.AttachEscapeToDismiss(this);

        RegisterMessages();
    }

    protected async override void OnAppearing()
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            // Always refetch: Shell caches this page across logouts, so relying on
            // _isFirstView left the previous user's drives on screen after re-login.
            List<Drive> drives = await _viewModel.FetchDrivesAsync();

            await InitializeChartAsync(drives);

            await HandleConnectDrivesOnStartupAsync();

            await _viewModel.InitializeCountdownAsync();

            // Started here rather than at app start: the watch set comes from GetDrives,
            // which requires a signed-in user.
            await _watchdog.StartAsync();

            // Same reason — the tray menu lists the signed-in user's drives.
            await _tray.StartAsync();

            await PruneAuditlogsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// Clears the once-per-session state so the next sign-in starts clean. Shell caches
    /// this page across a sign-out, so without this the startup pass — which also applies
    /// the signed-in user's language — only ever ran for the first user of the process.
    /// </summary>
    internal static void ResetSessionState()
    {
        _isFirstView = true;
        _prunedAuditlogs = false;
    }

    /// <summary>
    /// Trims the audit log to the user's retention setting, once per sign-in.
    /// </summary>
    /// <remarks>
    /// Here rather than on a timer because the log is only ever read from the audit page,
    /// so trimming it more often than a person can look at it buys nothing. Failures are
    /// logged and swallowed: housekeeping must never keep the dashboard off screen.
    /// </remarks>
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
        // Use the provided drives if they are not null
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

        // Batch lookup — DriveInfo.GetDrives() is enumerated once instead of per drive.
        HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();
        int connected = drives.Count(d => connectedLetters.Contains(d.Letter));

        return BuildEntries(connected, drives.Count - connected);
    }

    private static ChartEntry[] BuildEntries(int connected, int disconnected)
    {
        // Fully qualified: `Application` alone binds to the Helix.Application namespace here.
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
            // A thin ring reads as a gauge; the thick default reads as a pie.
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

        WeakReferenceMessenger.Default.Register<SearchDrivesMessage>(
            this, async (r, m) => await _modals.ToggleAsync(SearchDrives, m.Value));

        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(
            this, async (r, m) => await InitializeChartAsync());
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
