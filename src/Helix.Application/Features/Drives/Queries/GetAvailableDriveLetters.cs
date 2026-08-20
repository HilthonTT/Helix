using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

/// <summary>
/// The drive letters a new or edited drive could actually use.
/// </summary>
/// <remarks>
/// Uniqueness used to be checked against the user's own drives only, so a letter already
/// taken by a USB stick or another account's mapping saved happily and then failed at
/// connect time with a Windows error that named no field. This asks the operating system
/// as well, and the form offers only what is free.
///
/// On macOS a "letter" is a directory under the mount root, so the connector reports only
/// Helix's own mounts and nothing is excluded on the OS's behalf — which is correct there.
/// </remarks>
public sealed class GetAvailableDriveLetters(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    /// <param name="ExcludeDriveId">
    /// The drive being edited. Its own letter is offered even though it is in use —
    /// by itself — so that saving an unrelated change does not force a letter change too.
    /// </param>
    public sealed record Request(Guid? ExcludeDriveId = null);

    public async Task<Result<List<string>>> Handle(
        Request? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<string>>(AuthenticationErrors.InvalidPermissions);
        }

        Guid? excludeDriveId = request?.ExcludeDriveId;

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        var taken = new HashSet<string>(nasConnector.GetConnectedLetters(), StringComparer.OrdinalIgnoreCase);

        foreach (Drive drive in drives)
        {
            if (drive.Id != excludeDriveId)
            {
                taken.Add(drive.Letter);
            }
        }

        string? keep = drives.FirstOrDefault(d => d.Id == excludeDriveId)?.Letter;
        if (!string.IsNullOrWhiteSpace(keep))
        {
            taken.Remove(keep);
        }

        List<string> available = [.. Alphabet.Where(letter => !taken.Contains(letter))];

        return available;
    }

    private static IEnumerable<string> Alphabet =>
        Enumerable.Range('A', 26).Select(value => ((char)value).ToString());
}
