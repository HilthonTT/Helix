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
        bool ConnectByHostname = false,
        string? HomeNetworkId = null,
        string? HomeNetworkName = null,
        string? MacAddress = null,
        bool ApplyCredentialsToServer = false);

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

        }

        bool isSameShare = string.Equals(drive.Host, request.Host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(drive.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase);

        if ((!isSameLetter || !isSameShare) && nasConnector.IsMountedFrom(drive))
        {
            using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

            Result unmount = await nasConnector.DisconnectAsync(drive, cancellationToken);
            if (unmount.IsFailure)
            {
                return unmount;
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

        drive.PinToNetwork(request.HomeNetworkId, request.HomeNetworkName);

        drive.RememberMacAddress(request.MacAddress);

        if (request.ApplyCredentialsToServer)
        {
            List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

            foreach (Drive sibling in drives.Where(d => d.Id != drive.Id && d.IsOnSameServerAs(drive.Host)))
            {
                sibling.ChangeCredentials(drive.Username, drive.Password);
            }
        }

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

        if (!string.IsNullOrWhiteSpace(request.MacAddress) && !MacAddresses.IsValid(request.MacAddress))
        {
            return Result.Failure(DriveErrors.NotAMacAddress);
        }

        string[] properties = [request.Letter, request.Host, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
