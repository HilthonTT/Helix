using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class GetAvailableDriveLetters(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
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
