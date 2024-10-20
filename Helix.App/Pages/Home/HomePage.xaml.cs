using Microcharts;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Helix.App.Pages.Home;

public sealed partial class HomePage : ContentPage
{
	public HomePage()
	{
		InitializeComponent();

		BindingContext = new HomeViewModel();

        InitializeChart();
    }

	private void InitializeChart()
	{
        ChartEntry[] entries =
        [
            new ChartEntry(212)
			{
				Label = "Windows",
				ValueLabel = "112",
				Color = SKColor.Parse("#2c3e50"),
			},
            new ChartEntry(248)
            {
                Label = "Android",
                ValueLabel = "646",
                Color = SKColor.Parse("#77d065"),
            },
            new ChartEntry(212)
            {
                Label = ".NET Maui",
                ValueLabel = "214",
                Color = SKColor.Parse("#3498db"),
            },
        ];

        chartView.Chart = new RadialGaugeChart
        {
            Entries = entries,
            IsAnimated = true,
            LabelTextSize = 24,
            BackgroundColor = Colors.Transparent.ToSKColor(),
        };
    }
}