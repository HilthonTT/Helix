namespace Helix.Application.Abstractions.Updates;

/// <summary>
/// Downloads a release and puts it in place of the running one.
/// </summary>
/// <remarks>
/// Helix ships as a folder the user unzips, with no installer and no signing, so
/// "installing an update" means replacing that folder — and the folder is in use by the
/// process asking for it. The work is therefore split in two: <see cref="StageAsync"/>
/// does everything that can be done safely while Helix runs, and <see cref="Apply"/>
/// hands the last step to something that outlives it.
///
/// Staging deliberately happens in full before anything is replaced. A download that
/// fails, an archive that will not open, or a build that does not look like Helix all
/// fail with the installed copy untouched, which is the only state this feature must
/// never get wrong.
/// </remarks>
public interface IUpdateInstaller
{
    /// <summary>Whether this build can replace itself at all.</summary>
    /// <remarks>
    /// False where the app is not writable by the user running it — a copy under
    /// Program Files, most obviously — because the swap would fail halfway rather than
    /// not start.
    /// </remarks>
    bool IsSupported { get; }

    /// <summary>
    /// Downloads the update and unpacks it beside the install, without touching it.
    /// </summary>
    /// <returns>The folder holding the unpacked new version.</returns>
    Task<Result<string>> StageAsync(
        UpdateCheck update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hands the swap to a helper process and reports that it started.
    /// </summary>
    /// <remarks>
    /// The caller must quit immediately afterwards: the helper waits for this process to
    /// exit before it touches anything, and a Helix that changes its mind and keeps
    /// running is a Helix whose files are replaced underneath it.
    /// </remarks>
    Result Apply(string stagedDirectory);
}
