using Helix.App.Resources.Languages;
using Helix.Application.Features.Drives.Contracts;

namespace Helix.App.Models;

public sealed class DriveReliabilityDisplay(DriveReliability reliability)
{
    public string Title => string.Format(AppResources.ReliabilityTitle, reliability.WindowDays);

    public string Drops => reliability.Drops switch
    {
        0 => AppResources.ReliabilityNoDrops,
        1 => string.Format(AppResources.ReliabilityDropsOne, When(reliability.LastDropOnUtc)),
        _ => string.Format(
            AppResources.ReliabilityDropsMany,
            reliability.Drops,
            reliability.DropsLastWeek,
            When(reliability.LastDropOnUtc)),
    };

    public string Recovery => !reliability.HasDropped
        ? string.Empty
        : reliability.TypicalRecovery is TimeSpan typical
            ? string.Format(AppResources.ReliabilityRecovered, reliability.Recoveries, reliability.Drops, Duration(typical))
            : AppResources.ReliabilityNeverRecovered;

    public bool HasRecovery => reliability.HasDropped;

    public bool IsFlaky => reliability.DropsLastWeek >= FlakyDropsPerWeek;

    public bool IsSteady => !IsFlaky;

    private const int FlakyDropsPerWeek = 3;

    private static string When(DateTime? utc) => utc?.ToLocalTime().ToString("g") ?? string.Empty;

    private static string Duration(TimeSpan span) => span.TotalSeconds switch
    {
        < 90 => string.Format(AppResources.DurationSeconds, Math.Max(1, (int)Math.Round(span.TotalSeconds))),
        < 90 * 60 => string.Format(AppResources.DurationMinutes, (int)Math.Round(span.TotalMinutes)),
        _ => string.Format(AppResources.DurationHours, (int)Math.Round(span.TotalHours)),
    };
}
