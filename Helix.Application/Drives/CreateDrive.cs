using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class CreateDrive(IDbContext context, ILoggedInUser loggedInUser)
{
    public sealed record Request(string Letter, string IpAddress, string Name, string Username, string Password);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Drive>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions);
        }

        if (await context.Drives.ExistsWithLetterAsync(request.Letter, cancellationToken))
        {
            return Result.Failure<Drive>(DriveErrors.LetterNotUnique(request.Letter));
        }

        var drive = Drive.Create(
            loggedInUser.UserId,
            request.Letter, 
            request.IpAddress, 
            request.Name, 
            request.Username, 
            request.Password);

        context.Drives.Add(drive);

        await context.SaveChangesAsync(cancellationToken);

        return drive;
    }

    private static Result Validate(Request request)
    {
        if (request.Letter.Length > 1)
        {
            return Result.Failure(DriveErrors.NotALetter);
        }

        string[] properties = [request.Letter, request.IpAddress, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(Error.NullValue)
            : Result.Success();
    }
}
