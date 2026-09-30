using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Theming;

namespace Helix.App.Models;

public sealed partial class ThemeOptionDisplay : ObservableObject
{
    public ThemeOptionDisplay(ThemeChoice choice, bool isSelected)
    {
        Choice = choice;
        IsSelected = isSelected;

        if (choice == ThemeChoice.System)
        {
            CanvasBrush = Split(ThemePalettes.Light.Canvas, ThemePalettes.Dark.Canvas);
            SurfaceBrush = Split(ThemePalettes.Light.Surface, ThemePalettes.Dark.SurfaceAlt);
            AccentBrush = new SolidColorBrush(ThemePalettes.Light.Accent);
            SoftBrush = new SolidColorBrush(ThemePalettes.Light.AccentSoft);
        }
        else
        {
            ThemePalette palette = ThemePalettes.For(choice, systemIsDark: false);

            CanvasBrush = new SolidColorBrush(palette.Canvas);
            SurfaceBrush = new SolidColorBrush(palette.SurfaceRaised);
            AccentBrush = new SolidColorBrush(palette.Accent);
            SoftBrush = new SolidColorBrush(palette.AccentSoft);
        }
    }

    public ThemeChoice Choice { get; }

    public string Name => LocalizationResourceManager.Instance[$"Theme{Choice}"] as string ?? Choice.ToString();

    public Brush CanvasBrush { get; }

    public Brush SurfaceBrush { get; }

    public Brush AccentBrush { get; }

    public Brush SoftBrush { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    private static LinearGradientBrush Split(Color left, Color right) => new(
        [
            new GradientStop(left, 0f),
            new GradientStop(left, 0.5f),
            new GradientStop(right, 0.5f),
            new GradientStop(right, 1f)
        ],
        new Point(0, 0),
        new Point(1, 0));
}
