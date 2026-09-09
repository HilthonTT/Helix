namespace Helix.Application.Abstractions.Updates;

public static class UpdateErrors
{
    public static readonly Error Unreachable = Error.Problem(
        "Update.Unreachable",
        "Could not reach GitHub to check for updates. Check your internet connection and try again.");

    public static readonly Error NoReleases = Error.NotFound(
        "Update.NoReleases",
        "No published release was found to compare against.");

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

    public static readonly Error UnreadableDownload = Error.Problem(
        "Update.UnreadableDownload",
        "The downloaded update could not be read. Nothing has been changed; please try again.");

    public static readonly Error DownloadNotHelix = Error.Problem(
        "Update.DownloadNotHelix",
        "The downloaded update does not look like Helix, so it was not installed.");

    public static readonly Error DownloadCorrupt = Error.Problem(
        "Update.DownloadCorrupt",
        "The downloaded update does not match the checksum GitHub published for it, so it was not installed.");

    public static readonly Error SignatureMissing = Error.Problem(
        "Update.SignatureMissing",
        "This release is not signed, and this build of Helix only installs signed updates. Open the release page to fetch it by hand.");

    public static readonly Error SignatureInvalid = Error.Problem(
        "Update.SignatureInvalid",
        "The signature on the downloaded update does not match, so it was not installed. It was not built by whoever publishes Helix.");

    public static readonly Error NotWritable = Error.Problem(
        "Update.NotWritable",
        "Helix cannot replace its own files where it is installed. Update it by hand, or move it somewhere you own.");

    public static readonly Error UnsafeInstallLocation = Error.Problem(
        "Update.UnsafeInstallLocation",
        "Helix is running from a folder it shares with other files, so it cannot replace that folder safely. Move it into a folder of its own and try again.");

    public static Error InstallFailed(string message) => Error.Problem(
        "Update.InstallFailed",
        $"The update could not be started: {message}");

    public static readonly Error UnreadableRelease = Error.Problem(
        "Update.UnreadableRelease",
        "The latest release could not be read. It may not be tagged with a version number.");
}
