using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDrives(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result<List<Drive>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        var drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
 
        return drives;
    }
}
