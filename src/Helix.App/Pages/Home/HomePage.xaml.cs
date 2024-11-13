using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Create;
using Helix.App.Modals.Drives.Delete;
using Helix.App.Modals.Drives.Search;
using Helix.App.Modals.Drives.Update;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using Microcharts;
using SharedKernel;
using SkiaSharp.Views.Maui;

namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
    private static bool _isFirstView = true;

    private static bool _searchDrivesModalOpen = false;
    private static bool _deleteDriveModalOpen = false;
    private static bool _createDriveModalOpen = false;
    private static bool _updateDriveModalOpen = false;

    private readonly GetDrives _getDrives;
    private readonly INasConnector _nasConnector;
    private readonly HomeViewModel _viewModel;

    public HomePage()
	{
		InitializeComponent();

        _viewModel = new HomeViewModel();

        BindingContext = _viewModel;

        _getDrives = App.ServiceProvider.GetRequiredService<GetDrives>();
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();

        RegisterMessages();
    }

    protected async override void OnAppearing()
    {
        List<Drive>? drives = null;

        if (_isFirstView)
        {
            drives = await _viewModel.FetchDrivesAsync();
        }

        await InitializeChartAsync(drives);

        await HandleConnectDrivesOnStartupAsync();
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

    private void OpenModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        absoluteLayout.IsVisible = true;
        contentView.Opacity = 0;
        _ = contentView.FadeTo(1, 800, Easing.CubicIn);
        _ = BlockScreen.FadeTo(0.8, 800, Easing.CubicOut);

        BlockScreen.InputTransparent = false;
    }

    private async Task CloseModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        _ = contentView.FadeTo(0, 800, Easing.CubicOut);
        _ = BlockScreen.FadeTo(0, 800, Easing.CubicOut);
        BlockScreen.InputTransparent = true;

        await Task.Delay(800);
        absoluteLayout.IsVisible = false;
    }

    private async Task OpenSearchDrivesModalAsync(bool show) 
    {
        if (_createDriveModalOpen)
        {
            await OpenCreateDriveModalAsync(false);
        }

        if (_deleteDriveModalOpen)
        {
            await OpenDeleteDriveModalAsync(false);
        }

        if (_updateDriveModalOpen)
        {
            await OpenUpdateDriveModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(SearchDrivesLayout, SearchDrivesView);
            _searchDrivesModalOpen = true;
        }
        else
        {
            await CloseModalInternal(SearchDrivesLayout, SearchDrivesView);
            _searchDrivesModalOpen = false;
        }
    }

    private async Task OpenUpdateDriveModalAsync(bool show)
    {
        if (_createDriveModalOpen)
        {
            await OpenCreateDriveModalAsync(false);
        }

        if (_deleteDriveModalOpen)
        {
            await OpenDeleteDriveModalAsync(false);
        }

        if (_searchDrivesModalOpen)
        {
            await OpenSearchDrivesModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(UpdateDriveLayout, UpdateDriveView);
            _updateDriveModalOpen = true;
        }
        else
        {
            await CloseModalInternal(UpdateDriveLayout, UpdateDriveView);
            _updateDriveModalOpen = false;
        }
    }

    private async Task OpenDeleteDriveModalAsync(bool show)
    {
        if (_createDriveModalOpen)
        {
            await OpenCreateDriveModalAsync(false);
        }

        if (_updateDriveModalOpen)
        {
            await OpenUpdateDriveModalAsync(false);
        }

        if (_searchDrivesModalOpen)
        {
            await OpenSearchDrivesModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(DeleteDriveLayout, DeleteDriveView);
            _deleteDriveModalOpen = true;
        }
        else
        {
            await CloseModalInternal(DeleteDriveLayout, DeleteDriveView);
            _deleteDriveModalOpen = false;
        }
    }

    private async Task OpenCreateDriveModalAsync(bool show)
    {
        if (_deleteDriveModalOpen)
        {
            await OpenDeleteDriveModalAsync(false);
        }

        if (_updateDriveModalOpen)
        {
            await OpenUpdateDriveModalAsync(false);
        }

        if (_searchDrivesModalOpen)
        {
            await OpenSearchDrivesModalAsync(false);
        }

        if (show)
        {
            OpenModalInternal(CreateDriveLayout, CreateDriveView);
            _createDriveModalOpen = true;
        }
        else
        {
            await CloseModalInternal(CreateDriveLayout, CreateDriveView);
            _createDriveModalOpen = false;
        }
    }

    private async Task InitializeChartAsync(List<Drive>? providedDrives = null)
    {
        // Use the provided drives if they are not null
        List<Drive> drives = providedDrives ?? await FetchDrivesFromDatabaseAsync();

        ChartEntry[] entries = GenerateChartEntries(drives);
        chart.Chart = CreateDonutChart(entries);
    }

    private async Task<List<Drive>> FetchDrivesFromDatabaseAsync()
    {
        Result<List<Drive>> result = await _getDrives.Handle();
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
            return CreateDisconnectedEntries();
        }

        int connected = drives.Where(d => _nasConnector.IsConnected(d.Letter)).Count();
        int disconnected = drives.Count - connected;

        return
        [
            new ChartEntry(connected)
            {
                Color = Color.FromArgb("#50D1AA").ToSKColor(),
            },
            new ChartEntry(disconnected)
            {
                Color = Color.FromArgb("#EA7C69").ToSKColor(),
            }
        ];
    }

    private static ChartEntry[] CreateDisconnectedEntries()
    {
        return
        [
            new ChartEntry(0) // 0 connected
            {
                Color = Color.FromArgb("#50D1AA").ToSKColor(),
            },
            new ChartEntry(1) // 1 disconnected (represents all)
            {
                Color = Color.FromArgb("#EA7C69").ToSKColor(),
            }
        ];
    }

    private static DonutChart CreateDonutChart(ChartEntry[] entries)
    {
        return new DonutChart
        {
            Entries = entries,
            IsAnimated = true,
            LabelTextSize = 24,
            BackgroundColor = Colors.Transparent.ToSKColor(),
        };
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<CreateDriveMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _createDriveModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await OpenCreateDriveModalAsync(m.Value);
        });

        WeakReferenceMessenger.Default.Register<DeleteDriveMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _deleteDriveModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await OpenDeleteDriveModalAsync(m.Value);
        });

        WeakReferenceMessenger.Default.Register<UpdateDriveMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _updateDriveModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await OpenUpdateDriveModalAsync(m.Value);
        });

        WeakReferenceMessenger.Default.Register<SearchDrivesMessage>(this, async (r, m) =>
        {
            bool isAlreadyOpen = _searchDrivesModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await OpenSearchDrivesModalAsync(m.Value);
        });

        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(this, async (r, m) =>
        {
            await InitializeChartAsync();
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
