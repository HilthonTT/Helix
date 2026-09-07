namespace Helix.Application.Abstractions.Updates;

public sealed record UpdateCheck(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string? DownloadUrl = null,
    string? AssetName = null,
    string? AssetDigest = null)
{
    public bool CanInstall => IsUpdateAvailable && !string.IsNullOrWhiteSpace(DownloadUrl);
}

public interface IUpdateChecker
{
    Task<Result<UpdateCheck>> CheckAsync(CancellationToken cancellationToken = default);
}
