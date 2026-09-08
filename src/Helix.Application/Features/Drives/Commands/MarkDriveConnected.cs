using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class MarkDriveConnected(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public sealed record Request(string Letter);

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

        string letter = request.Letter.Trim().ToUpperInvariant();

        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        Drive? drive = drives.Find(candidate =>
            string.Equals(candidate.Letter, letter, StringComparison.OrdinalIgnoreCase));

        if (drive is null)
        {
            return Result.Failure<Drive>(DriveErrors.LetterNotFound(letter));
        }

        if (!nasConnector.IsMountedFrom(drive))
        {
            return Result.Failure<Drive>(DriveErrors.MountNotAvailable);
        }

        drive.MarkConnected(dateTimeProvider.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(drive);
    }

    private static Result Validate(Request request) => GeneralValidation.IsDriveLetter(request.Letter)
        ? Result.Success()
        : Result.Failure(DriveErrors.NotALetter);
}
