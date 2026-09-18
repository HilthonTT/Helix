using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class GetUnmanagedMappings(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    public async Task<Result<List<MappedShare>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<MappedShare>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        HashSet<string> managed = new(drives.Select(d => d.Letter), StringComparer.OrdinalIgnoreCase);

        return nasConnector.GetMappedShares()
            .Where(mapping => !managed.Contains(mapping.Letter))
            .OrderBy(mapping => mapping.Letter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
