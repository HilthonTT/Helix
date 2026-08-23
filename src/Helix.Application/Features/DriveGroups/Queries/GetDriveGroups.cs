using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Queries;

public sealed class GetDriveGroups(
    IDriveGroupRepository driveGroupRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result<List<DriveGroup>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<DriveGroup>>(AuthenticationErrors.InvalidPermissions);
        }

        List<DriveGroup> groups = await driveGroupRepository.GetAsNoTrackingAsync(
            loggedInUser.UserId,
            cancellationToken);

        // Named order, because this list is rendered as a row of buttons and a set of
        // buttons that moves between refreshes is a set of buttons that gets misclicked.
        return groups.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
