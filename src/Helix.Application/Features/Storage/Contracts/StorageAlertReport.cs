namespace Helix.Application.Features.Storage.Contracts;

/// <summary>
/// What one storage check found: the volumes below the threshold, and every volume it
/// managed to measure.
/// </summary>
/// <remarks>
/// The second list is what lets the caller forget a warning correctly. A volume is only
/// "no longer short of space" if it was measured and found fine; one that was simply not
/// measured this time - its drives unmounted, or its probe past the timeout - is neither,
/// and forgetting it re-fired the warning at the next check.
/// </remarks>
public sealed record StorageAlertReport(
    IReadOnlyList<StorageAlert> Alerts,
    IReadOnlySet<string> MeasuredVolumeIds)
{
    public static readonly StorageAlertReport Empty = new([], new HashSet<string>());
}
