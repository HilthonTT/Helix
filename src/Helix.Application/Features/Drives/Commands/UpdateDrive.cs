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

        // Letters are stored uppercase, so compare case-insensitively — otherwise
        // re-saving your own drive with a lowercase letter is falsely rejected.
        bool isSameLetter = string.Equals(drive.Letter, request.Letter, StringComparison.OrdinalIgnoreCase);
        if (!isSameLetter)
        {
            if (!await driveRepository.IsLetterUniqueAsync(request.Letter, loggedInUser.UserId, cancellationToken))
            {
                return Result.Failure(DriveErrors.LetterNotUnique(request.Letter));
            }

            // Only when the letter is actually changing. This drive's own letter is in
            // use by this drive whenever it is connected, and rejecting that would make
            // an edit to any other field impossible while the share was mounted.
            if (nasConnector.GetConnectedLetters().Contains(request.Letter.ToUpperInvariant()))
            {
                return Result.Failure(DriveErrors.LetterInUse(request.Letter));
            }

            // The old letter is about to belong to no row. Left mounted, a persistent
            // mapping would be restored by Explorer at every sign-in with nothing in
            // Helix able to remove it — the orphan DeleteDrive exists to prevent. Best
            // effort, like there: an unreachable share must not block the edit.
            if (nasConnector.GetConnectedLetters().Contains(drive.Letter))
            {
                using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

                await nasConnector.DisconnectAsync(drive, cancellationToken);
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
