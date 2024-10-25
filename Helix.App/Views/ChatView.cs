using Microcharts;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Helix.App.Views;

internal sealed class ChartView : SKCanvasView
{
    public event EventHandler<SKPaintSurfaceEventArgs>? ChartPainted;

    public static readonly BindableProperty ChartProperty = BindableProperty.Create(
        nameof(Chart),
        typeof(Chart),
        typeof(ChartView),
        null,
        propertyChanged: OnChartChanged);


    private InvalidatedWeakEventHandler<ChartView>? _handler;

    private Chart? _chart;

    public Chart Chart
    {
        get { return (Chart)GetValue(ChartProperty); }
        set { SetValue(ChartProperty, value); }
    }

    public ChartView()
    {
        try
        {
            Background = Colors.Transparent;
            PaintSurface += OnPaintCanvas;
        }
        catch (Exception ex)
        {
            Console.Write(ex);
        }
    }

    private static void OnChartChanged(BindableObject d, object oldValue, object value)
    {
        if (d is not ChartView view)
        {
            return;
        }

        if (view._chart is not null)
        {
            view._handler?.Dispose();
            view._handler = null;
        }

        view._chart = value as Chart;
        view.InvalidateSurface();

        if (view._chart is not null)
        {
            view._handler = view?._chart.ObserveInvalidate(view, (v) => v?.InvalidateSurface());
        }
    }

    private void OnPaintCanvas(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_chart is not null)
        {
            _chart.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        }
        else
        {
            e.Surface.Canvas.Clear(SKColors.Transparent);
        }

        ChartPainted?.Invoke(sender, e);
    }
}
