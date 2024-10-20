using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDrives(IDbContext context, ILoggedInUser loggedInUser)
{
    public async Task<Result<List<Drive>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await context.Drives
            .Where(d => d.UserId == loggedInUser.UserId)
            .ToListAsync(cancellationToken);

        return drives;
    }
}
