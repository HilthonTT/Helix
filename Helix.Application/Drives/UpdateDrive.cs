using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class UpdateDrive(IDbContext context, ILoggedInUser loggedInUser)
{
    public sealed record Request(Guid DriveId, string Letter, string IpAddress, string Name, string Username, string Password);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Drive>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await context.Drives.GetByIdAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure(DriveErrors.NotFound(request.DriveId));
        }

        if (await context.Drives.ExistsWithLetterAsync(request.Letter, cancellationToken)
            && drive.Letter != request.Letter)
        {
            return Result.Failure(DriveErrors.LetterNotUnique(request.Letter));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        drive.Update(request.Letter, request.IpAddress, request.Name, request.Username, request.Password);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        string[] properties = [request.Letter, request.IpAddress, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(Error.NullValue)
            : Result.Success();
    }
}
