using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class CreateDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    public sealed record NewDrive(string Letter, string Host, string Name);

    public sealed record Request(
        IReadOnlyList<NewDrive> Drives,
        string Username,
        string Password,
        bool AutoConnect = true,
        bool Persistent = false,
        bool ConnectByHostname = false,
        string? HomeNetworkId = null,
        string? HomeNetworkName = null,
        string? MacAddress = null,
        string? RemoteHost = null);

    public async Task<Result<List<Drive>>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<List<Drive>>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        HashSet<string> connectedLetters = nasConnector.GetConnectedLetters();

        List<Drive> created = [];

        foreach (NewDrive entry in request.Drives)
        {
            if (!await driveRepository.IsLetterUniqueAsync(entry.Letter, loggedInUser.UserId, cancellationToken))
            {
                return Result.Failure<List<Drive>>(DriveErrors.LetterNotUnique(entry.Letter));
            }

            var drive = Drive.Create(
                loggedInUser.UserId,
                entry.Letter,
                entry.Host,
                entry.Name,
                request.Username,
                request.Password,
                request.AutoConnect,
                request.Persistent,
                request.ConnectByHostname);

            drive.PinToNetwork(request.HomeNetworkId, request.HomeNetworkName);

            drive.RememberMacAddress(request.MacAddress);

            drive.ReachAwayAt(request.RemoteHost);

            if (connectedLetters.Contains(drive.Letter) && !nasConnector.IsMountedFrom(drive))
            {
                return Result.Failure<List<Drive>>(DriveErrors.LetterInUse(entry.Letter));
            }

            created.Add(drive);
        }

        foreach (Drive drive in created)
        {
            driveRepository.Insert(drive);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return created;
    }

    private static Result Validate(Request request)
    {
        if (request.Drives.Count == 0)
        {
            return Result.Failure(DriveErrors.NothingToCreate);
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        if (!string.IsNullOrWhiteSpace(request.MacAddress) && !MacAddresses.IsValid(request.MacAddress))
        {
            return Result.Failure(DriveErrors.NotAMacAddress);
        }

        if (!string.IsNullOrWhiteSpace(request.RemoteHost) && !GeneralValidation.IsValidHost(request.RemoteHost))
        {
            return Result.Failure(DriveErrors.InvalidRemoteHost);
        }

        HashSet<string> letters = new(StringComparer.OrdinalIgnoreCase);

        foreach (NewDrive entry in request.Drives)
        {
            if (!GeneralValidation.IsDriveLetter(entry.Letter))
            {
                return Result.Failure(DriveErrors.NotALetter);
            }

            if (!GeneralValidation.IsValidHost(entry.Host))
            {
                return Result.Failure(ValidationErrors.InvalidHost);
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                return Result.Failure(ValidationErrors.MissingFields);
            }

            if (!letters.Add(entry.Letter))
            {
                return Result.Failure(DriveErrors.DuplicateLetter(entry.Letter.ToUpperInvariant()));
            }
        }

        return Result.Success();
    }
}
