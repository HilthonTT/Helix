﻿using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class UpdateDrive(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser, 
    ICacheService cacheService) : IHandler
{
    public sealed record Request(
        Guid DriveId, 
        string Letter, 
        string IpAddress, 
        string Name, 
        string Username, 
        string Password);

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

        Drive? drive = await driveRepository.GetByIdAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure(DriveErrors.NotFound(request.DriveId));
        }

        if (!await driveRepository.IsLetterUniqueAsync(request.Letter, loggedInUser.UserId, cancellationToken)
            && drive.Letter != request.Letter)
        {
            return Result.Failure(DriveErrors.LetterNotUnique(request.Letter));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        drive.Update(request.Letter, request.IpAddress, request.Name, request.Username, request.Password);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.All, cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.GetById(request.DriveId), cancellationToken);

        return Result.Success();
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
