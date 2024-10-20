using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Drives.Create;
using Microcharts;
using SkiaSharp.Views.Maui;

namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
    private static bool _createDriveModalOpen = false;

    public HomePage()
	{
		InitializeComponent();

		BindingContext = new HomeViewModel();

        InitializeChart();
        RegisterMessages();
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

	private void InitializeChart()
	{
        ChartEntry[] entries =
        [
           new ChartEntry(3)
           { 
               Color = Color.FromArgb("#50D1AA").ToSKColor(),
           },
           new ChartEntry(2) 
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
    }
}