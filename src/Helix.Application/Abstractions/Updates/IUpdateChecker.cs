namespace Helix.Application.Abstractions.Updates;

/// <summary>The outcome of comparing this build against the latest published release.</summary>
/// <param name="CurrentVersion">What is running, as the user would recognise it.</param>
/// <param name="LatestVersion">
/// The release's tag, verbatim — <c>v2.1.0</c> rather than a normalized number, so it
/// matches what is on the releases page the user is about to open.
/// </param>
/// <param name="ReleaseUrl">The release's page, for the "open" action.</param>
public sealed record UpdateCheck(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl);

/// <summary>
/// Asks whether a newer Helix has been published.
/// </summary>
/// <remarks>
/// Helix ships as a folder the user unzips themselves, so nothing tells them a new version
/// exists — releases are tagged and published on GitHub and then sit there unnoticed. This
/// is the manual "check now" behind the Settings button; it downloads nothing and installs
/// nothing, it only reports and offers to open the release page.
/// </remarks>
public interface IUpdateChecker
{
    Task<Result<UpdateCheck>> CheckAsync(CancellationToken cancellationToken = default);
}
