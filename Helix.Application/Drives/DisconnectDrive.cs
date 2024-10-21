using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class DisconnectDrive(IDbContext context, ILoggedInUser loggedInUser, INasConnector nasConnector)
{
    public sealed record Request(string Letter);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await context.Drives
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Letter == request.Letter && d.UserId == loggedInUser.UserId,
                cancellationToken);

        if (drive is null)
        {
            return Result.Failure(DriveErrors.LetterNotFound(request.Letter));
        }

        return await nasConnector.DisconnectAsync(drive, cancellationToken);
    }
}
