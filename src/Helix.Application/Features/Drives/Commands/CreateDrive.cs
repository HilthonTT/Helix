using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class CreateDrive(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    /// <param name="AutoConnect">
    /// Whether the drive joins the unattended connect passes. Defaults to true so a
    /// drive added without thinking about it behaves the way every drive did before
    /// the flag existed.
    /// </param>
    /// <param name="Persistent">
    /// Whether Windows should remember the mapping across sign-ins. Defaults to false:
    /// a mapping that outlives the app is a change to the user machine, so it is opted
    /// into rather than out of.
    /// </param>
    public sealed record Request(
        string Letter,
        string Host,
        string Name,
        string Username,
        string Password,
        bool AutoConnect = true,
        bool Persistent = false);

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

        // The repository only knows about this user's drives. A letter taken by a USB
        // stick, an optical drive or another account's mapping used to save fine and then
        // fail at connect time with a Windows error that named no field.
        if (nasConnector.GetConnectedLetters().Contains(request.Letter.ToUpperInvariant()))
        {
            return Result.Failure<Drive>(DriveErrors.LetterInUse(request.Letter));
        }

        var drive = Drive.Create(
            loggedInUser.UserId,
            request.Letter, 
            request.Host, 
            request.Name,
            request.Username,
            request.Password,
            request.AutoConnect,
            request.Persistent);

        driveRepository.Insert(drive);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return drive;
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Letter) || request.Letter.Length != 1 || !char.IsLetter(request.Letter[0]))
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
