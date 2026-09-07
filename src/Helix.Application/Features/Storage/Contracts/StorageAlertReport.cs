namespace Helix.Application.Features.Storage.Contracts;

public sealed record StorageAlertReport(
    IReadOnlyList<StorageAlert> Alerts,
    IReadOnlySet<string> MeasuredVolumeIds)
{
    public static readonly StorageAlertReport Empty = new([], new HashSet<string>());
}
