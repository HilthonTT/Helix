using Helix.App.Resources.Languages;
using Helix.App.Theming;
using Helix.Application.Features.Storage.Contracts;
using Microcharts;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Globalization;

namespace Helix.App.Models;

public sealed class VolumeHistoryDisplay
{
    private const double BytesPerGigabyte = 1_000_000_000d;

    private readonly VolumeHistory _volume;

    public VolumeHistoryDisplay(VolumeHistory volume)
    {
        _volume = volume;
        Chart = BuildChart(volume.Points);
    }

    public string Title => string.Join(", ", _volume.DriveNames);

    public string Usage => StorageUsageHelper.FormatCombined(
        _volume.TotalBytes - _volume.FreeBytes,
        _volume.TotalBytes);

    public string UsedPercent => _volume.TotalBytes <= 0
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            AppResources.StorageUsedPercent,
            Math.Round(100d * (_volume.TotalBytes - _volume.FreeBytes) / _volume.TotalBytes));

    public string Trend => (_volume.BytesPerDay, _volume.DaysUntilFull) switch
    {
        (null, _) => AppResources.StorageTrendNotEnoughHistory,
        (double rate, int days) when rate < 0 => days switch
        {
            < 1 => AppResources.StorageFullWithinADay,
            < 60 => string.Format(AppResources.StorageFullInDays, days),
            _ => string.Format(AppResources.StorageFullInMonths, (int)Math.Round(days / 30.0)),
        },
        (double rate, _) when rate < 0 => string.Format(
            AppResources.StorageTrendShrinking,
            (-rate / BytesPerGigabyte).ToString("F1", CultureInfo.CurrentCulture)),
        _ => AppResources.StorageTrendSteady,
    };

    public bool IsFillingUp => _volume.DaysUntilFull is not null;

    public string Span => _volume.Points.Count == 0
        ? string.Empty
        : string.Format(
            AppResources.StorageHistorySpan,
            _volume.Points[0].Day.ToString("d", CultureInfo.CurrentCulture),
            _volume.Points[^1].Day.ToString("d", CultureInfo.CurrentCulture),
            _volume.Points.Count);

    public Chart Chart { get; }

    private static LineChart BuildChart(IReadOnlyList<StoragePoint> points)
    {
        ThemePalette palette = ThemeSwitcher.Palette;
        SKColor line = palette.Accent.ToSKColor();

        ChartEntry[] entries =
        [
            .. points.Select(point => new ChartEntry(UsedPercentOf(point)) { Color = line }),
        ];

        return new LineChart
        {
            Entries = entries,
            LineMode = LineMode.Straight,
            LineSize = 3,
            PointMode = points.Count > 45 ? PointMode.None : PointMode.Circle,
            PointSize = 6,
            LineAreaAlpha = 40,
            MinValue = 0,
            MaxValue = 100,
            IsAnimated = false,
            LabelTextSize = 0,
            ValueLabelOption = ValueLabelOption.None,
            BackgroundColor = SKColors.Transparent,
        };
    }

    private static float UsedPercentOf(StoragePoint point) => point.TotalBytes <= 0
        ? 0
        : (float)(100d * (point.TotalBytes - point.FreeBytes) / point.TotalBytes);
}
