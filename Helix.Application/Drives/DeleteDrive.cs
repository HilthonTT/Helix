using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class DeleteDrive(IDbContext context, ILoggedInUser loggedInUser)
{
    public sealed record Request(Guid Id);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure(validationResult.Error);
        }

        Drive? drive = await context.Drives.FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);
        if (drive is null)
        {
            return Result.Failure(DriveErrors.NotFound(request.Id));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        context.Drives.Remove(drive);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (request.Id == Guid.Empty)
        {
            return Result.Failure(Error.NullValue);
        }

        return Result.Success();
    }
}
