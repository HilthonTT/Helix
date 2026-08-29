namespace Helix.Application.Abstractions.Updates;

/// <summary>
/// Failures from the update check.
/// </summary>
/// <remarks>
/// Public and next to <see cref="IUpdateChecker"/> rather than in <c>Core/Errors</c>,
/// because the implementation lives in Infrastructure and returns these — the same
/// reasoning as <c>DiagnosticsErrors</c>.
/// </remarks>
public static class UpdateErrors
{
    public static readonly Error Unreachable = Error.Problem(
        "Update.Unreachable",
        "Could not reach GitHub to check for updates. Check your internet connection and try again.");

    public static readonly Error NoReleases = Error.NotFound(
        "Update.NoReleases",
        "No published release was found to compare against.");

    /// <remarks>
    /// GitHub allows 60 unauthenticated calls an hour per address. Worth its own message:
    /// "try again later" is actionable, whereas a bare 403 reads like something is broken.
    /// </remarks>
    public static readonly Error RateLimited = Error.Problem(
        "Update.RateLimited",
        "GitHub is rate-limiting update checks right now. Please try again later.");

    public static Error UnexpectedResponse(int statusCode) => Error.Problem(
        "Update.UnexpectedResponse",
        $"GitHub answered the update check with an unexpected status ({statusCode}).");

    public static readonly Error NoAsset = Error.NotFound(
        "Update.NoAsset",
        "This release has no download built for this computer. Open the release page to fetch it by hand.");

    public static readonly Error DownloadFailed = Error.Problem(
        "Update.DownloadFailed",
        "The update could not be downloaded. Check your internet connection and try again.");

    /// <remarks>
    /// Covers a truncated download and an archive that is not what it claims. Both mean
    /// the same thing to the user and neither is worth distinguishing: nothing has been
    /// replaced, and trying again is the answer to both.
    /// </remarks>
    public static readonly Error UnreadableDownload = Error.Problem(
        "Update.UnreadableDownload",
        "The downloaded update could not be read. Nothing has been changed; please try again.");

    /// <remarks>
    /// The check that stops a broken or unexpected archive from being copied over a
    /// working install. It has never fired in practice, which is rather the point.
    /// </remarks>
    public static readonly Error DownloadNotHelix = Error.Problem(
        "Update.DownloadNotHelix",
        "The downloaded update does not look like Helix, so it was not installed.");

    /// <remarks>
    /// GitHub publishes a SHA-256 for every release asset, and this is what happens when
    /// what arrived does not match it. Distinct from <see cref="UnreadableDownload"/>
    /// because the archive may well open perfectly well and simply not be the file that
    /// was published — a truncated transfer, a proxy that rewrote it, a cache serving
    /// something stale. Nothing has been replaced either way.
    /// </remarks>
    public static readonly Error DownloadCorrupt = Error.Problem(
        "Update.DownloadCorrupt",
        "The downloaded update does not match the checksum GitHub published for it, so it was not installed.");

    public static readonly Error NotWritable = Error.Problem(
        "Update.NotWritable",
        "Helix cannot replace its own files where it is installed. Update it by hand, or move it somewhere you own.");

    public static Error InstallFailed(string message) => Error.Problem(
        "Update.InstallFailed",
        $"The update could not be started: {message}");

    public static readonly Error UnreadableRelease = Error.Problem(
        "Update.UnreadableRelease",
        "The latest release could not be read. It may not be tagged with a version number.");
}
