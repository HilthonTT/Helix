using Microsoft.Maui.Controls.Shapes;

namespace Helix.App.Controls;

internal sealed class Meter : ContentView
{
    private const string FillAnimation = "Helix.Meter.Fill";

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double),
        typeof(Meter),
        0d,
        propertyChanged: (bindable, _, _) => ((Meter)bindable).Render(animate: true));

    public static readonly BindableProperty FillKeyProperty = BindableProperty.Create(
        nameof(FillKey),
        typeof(string),
        typeof(Meter),
        "Accent",
        propertyChanged: (bindable, _, value) => ((Meter)bindable)._fill.SetDynamicResource(BackgroundColorProperty, (string)value));

    private readonly Border _fill;

    public Meter()
    {
        HeightRequest = 6;

        Border track = new()
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 }
        };

        track.SetDynamicResource(BackgroundColorProperty, "BorderStrong");

        _fill = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            HorizontalOptions = LayoutOptions.Start,
            WidthRequest = 0
        };

        _fill.SetDynamicResource(BackgroundColorProperty, FillKey);

        Content = new Grid { Children = { track, _fill } };
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string FillKey
    {
        get => (string)GetValue(FillKeyProperty);
        set => SetValue(FillKeyProperty, value);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Render(animate: false);
    }

    private void Render(bool animate)
    {
        if (Width <= 0)
        {
            return;
        }

        double progress = double.IsNaN(Progress) ? 0 : Math.Clamp(Progress, 0, 1);
        double target = progress <= 0 ? 0 : Math.Max(Height, Width * progress);

        this.AbortAnimation(FillAnimation);

        if (!animate)
        {
            _fill.WidthRequest = target;
            return;
        }

        double start = _fill.WidthRequest < 0 ? 0 : _fill.WidthRequest;

        new Animation(value => _fill.WidthRequest = value, start, target, Easing.CubicOut)
            .Commit(this, FillAnimation, length: 420);
    }
}
