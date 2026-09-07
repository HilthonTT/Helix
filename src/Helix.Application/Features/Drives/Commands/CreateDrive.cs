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
    public sealed record Request(
        string Letter,
        string Host,
        string Name,
        string Username,
        string Password,
        bool AutoConnect = true,
        bool Persistent = false,
        bool ConnectByHostname = false);

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
            request.Host,
            request.Name,
            request.Username,
            request.Password,
            request.AutoConnect,
            request.Persistent,
            request.ConnectByHostname);

        if (nasConnector.GetConnectedLetters().Contains(drive.Letter) && !nasConnector.IsMountedFrom(drive))
        {
            return Result.Failure<Drive>(DriveErrors.LetterInUse(request.Letter));
        }

        driveRepository.Insert(drive);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return drive;
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
