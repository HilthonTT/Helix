using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class CreateDrive(
    IDriveRepository driveRepository, 
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser, 
    ICacheService cacheService) : IHandler
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

        if (!await driveRepository.IsLetterUniqueAsync(request.Letter, loggedInUser.UserId, cancellationToken))
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

        driveRepository.Insert(drive);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.All, cancellationToken);

        return drive;
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Letter) || request.Letter.Length != 1 || !char.IsLetter(request.Letter[0]))
        {
            return Result.Failure(DriveErrors.NotALetter);
        }

        if (!GeneralValidation.IsValidIpAddress(request.IpAddress))
        {
            return Result.Failure(ValidationErrors.InvalidIpAddress);
        }

        string[] properties = [request.Letter, request.IpAddress, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
