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

    public static readonly Error UnreadableRelease = Error.Problem(
        "Update.UnreadableRelease",
        "The latest release could not be read. It may not be tagged with a version number.");
}
