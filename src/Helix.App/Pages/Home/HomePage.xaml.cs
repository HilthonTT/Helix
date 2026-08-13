using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Helpers;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Create;
using Helix.App.Modals.Drives.Delete;
using Helix.App.Modals.Drives.Search;
using Helix.App.Modals.Drives.Update;
using Helix.App.Services;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using Microcharts;
using SharedKernel;
using SkiaSharp.Views.Maui;

namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
    private const string CreateDrive = "create-drive";
    private const string UpdateDrive = "update-drive";
    private const string DeleteDrive = "delete-drive";
    private const string SearchDrives = "search-drives";

    private static bool _isFirstView = true;
    private bool _isInitializing;

    private readonly INasConnector _nasConnector;
    private readonly HomeViewModel _viewModel;
    private readonly ModalHost _modals;
    private readonly DriveWatchdog _watchdog;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = new HomeViewModel();

        BindingContext = _viewModel;

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _watchdog = App.ServiceProvider.GetRequiredService<DriveWatchdog>();

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
