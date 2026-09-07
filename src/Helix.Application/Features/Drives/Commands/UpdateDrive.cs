using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class UpdateDrive(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor) : IHandler
{
    public sealed record Request(
        Guid DriveId,
        string Letter,
        string Host,
        string Name,
        string Username,
        string Password,
        bool AutoConnect = true,
        bool Persistent = false,
        bool ConnectByHostname = false);

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

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        bool isSameLetter = string.Equals(drive.Letter, request.Letter, StringComparison.OrdinalIgnoreCase);
        if (!isSameLetter)
        {
            if (!await driveRepository.IsLetterUniqueAsync(request.Letter, loggedInUser.UserId, cancellationToken))
            {
                return Result.Failure(DriveErrors.LetterNotUnique(request.Letter));
            }

            if (nasConnector.GetConnectedLetters().Contains(request.Letter.ToUpperInvariant()))
            {
                var candidate = Drive.Create(
                    loggedInUser.UserId,
                    request.Letter,
                    request.Host,
                    request.Name,
                    request.Username,
                    request.Password);

                if (!nasConnector.IsMountedFrom(candidate))
                {
                    return Result.Failure(DriveErrors.LetterInUse(request.Letter));
                }
            }

            if (nasConnector.GetConnectedLetters().Contains(drive.Letter))
            {
                using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

                Result unmount = await nasConnector.DisconnectAsync(drive, cancellationToken);
                if (unmount.IsFailure)
                {
                    return unmount;
                }
            }
        }

        drive.Update(
            request.Letter,
            request.Host,
            request.Name,
            request.Username,
            request.Password,
            request.AutoConnect,
            request.Persistent,
            request.ConnectByHostname);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (!GeneralValidation.IsDriveLetter(request.Letter))
        {
            return Result.Failure(DriveErrors.NotALetter);
        }

        if (!GeneralValidation.IsValidHost(request.Host))
        {
            return Result.Failure(ValidationErrors.InvalidHost);
        }

        string[] properties = [request.Letter, request.Host, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
