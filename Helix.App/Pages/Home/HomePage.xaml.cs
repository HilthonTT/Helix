using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Create;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using Microcharts;
using SharedKernel;
using SkiaSharp.Views.Maui;

namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
    private static bool _createDriveModalOpen = false;

    private readonly GetDrives _getDrives;
    private readonly INasConnector _nasConnector;

    public HomePage()
	{
		InitializeComponent();

		BindingContext = new HomeViewModel();

        _getDrives = App.ServiceProvider.GetRequiredService<GetDrives>();
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();

        RegisterMessages();
    }

    protected async override void OnAppearing()
    {
        await InitializeChartAsync();
    }

    private async Task OpenCreateDriveModalAsync(bool show)
    {
        if (show)
        {
            CreateDriveLayout.IsVisible = true;
            CreateDriveView.Opacity = 0;
            _ = CreateDriveView.FadeTo(1, 800, Easing.CubicIn);
            _ = BlockScreen.FadeTo(0.8, 800, Easing.CubicOut);

            BlockScreen.InputTransparent = false;
            _createDriveModalOpen = true;
        }
        else
        {
            _ = CreateDriveView.FadeTo(0, 800, Easing.CubicOut);
            _ = BlockScreen.FadeTo(0, 800, Easing.CubicOut);
            BlockScreen.InputTransparent = true;

            await Task.Delay(800);
            CreateDriveLayout.IsVisible = false;
            _createDriveModalOpen = false;
        }
    }

	private async Task InitializeChartAsync()
	{
        Result<List<Drive>> result = await _getDrives.Handle();
        if (result.IsFailure)
        {
            return;
        }

        IEnumerable<Drive> connected = result.Value.Where(d => _nasConnector.IsConnected(d.Letter));
        IEnumerable<Drive> disconnected = result.Value.Where(d => !_nasConnector.IsConnected(d.Letter));

        ChartEntry[] entries =
        [
           new ChartEntry(connected.Count())
           { 
               Color = Color.FromArgb("#50D1AA").ToSKColor(),
           },
           new ChartEntry(disconnected.Count()) 
           { 
               Color = Color.FromArgb("#EA7C69").ToSKColor(), 
           },
        ];

        chart.Chart = new DonutChart
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

        WeakReferenceMessenger.Default.Register<CheckDrivesStatusMessage>(this, async (r, m) =>
        {
            await InitializeChartAsync();
        });
    }
}