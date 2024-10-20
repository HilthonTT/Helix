using Microcharts;
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
}