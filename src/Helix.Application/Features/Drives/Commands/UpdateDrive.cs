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
        bool ApplyCredentialsToServer = false,
        string? RemoteHost = null,
        DriveProtocol Protocol = DriveProtocol.Smb,
        int? Port = null);

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
                    request.Password,
                    protocol: request.Protocol,
                    port: request.Port);

                candidate.ReachAwayAt(request.RemoteHost);

                if (!nasConnector.IsMountedFrom(candidate))
                {
                    return Result.Failure(DriveErrors.LetterInUse(request.Letter));
                }
            }

        }

        bool isSameShare = string.Equals(drive.Host, request.Host.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(drive.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                           drive.Protocol == request.Protocol &&
                           drive.Port == DriveProtocols.PortFor(request.Protocol, request.Port);

        bool unmounted = false;

        if ((!isSameLetter || !isSameShare || IsMountedThroughAnOldAddressForAway(drive, request)) &&
            nasConnector.IsMountedFrom(drive))
        {
            using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

            unmounted = true;

            Result unmount = await nasConnector.DisconnectAsync(drive, cancellationToken);
            if (unmount.IsFailure)
            {
                return unmount;
            }
        }

        bool persistenceChanged = drive.Persistent != request.Persistent;

        drive.Update(
            request.Letter,
            request.Host,
            request.Name,
            request.Username,
            request.Password,
            request.AutoConnect,
            request.Persistent,
            request.ConnectByHostname,
            request.Protocol,
            request.Port);

        drive.PinToNetwork(request.HomeNetworkId, request.HomeNetworkName);

        drive.RememberMacAddress(request.MacAddress);

        drive.ReachAwayAt(request.RemoteHost);

        if (request.ApplyCredentialsToServer && drive.Protocol.UsesCredentials())
        {
            List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

            foreach (Drive sibling in drives.Where(d => d.Id != drive.Id &&
                                                               d.Protocol.UsesCredentials() &&
                                                               d.IsOnSameServerAs(drive.Host)))
            {
                sibling.ChangeCredentials(drive.Username, drive.Password);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return persistenceChanged && !unmounted
            ? await ReapplyPersistenceAsync(drive, cancellationToken)
            : Result.Success();
    }

    /// <summary>
    /// Whether a mapping is remembered is fixed when it is made, so a switch flipped on a mounted
    /// drive did nothing: Windows went on restoring one the user had made temporary, and never
    /// restored one made persistent. The mapping is made again under the new flag — but only
    /// when nothing has a file open on it, since that means pulling the share out for a moment.
    /// Otherwise, and where the count is unknown, the switch applies at the next connect.
    /// </summary>
    private async Task<Result> ReapplyPersistenceAsync(Drive drive, CancellationToken cancellationToken)
    {
        if (!nasConnector.IsMountedFrom(drive))
        {
            return Result.Success();
        }

        bool live = nasConnector.IsLiveFrom(drive);

        if (live && nasConnector.CountOpenHandles(drive.Letter) is not 0)
        {
            return Result.Success();
        }

        using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

        Result unmount = await nasConnector.DisconnectAsync(drive, cancellationToken);
        if (unmount.IsFailure || !live)
        {
            return unmount;
        }

        return await nasConnector.ConnectAsync(drive, cancellationToken);
    }

    private bool IsMountedThroughAnOldAddressForAway(Drive drive, Request request)
    {
        string? requested = string.IsNullOrWhiteSpace(request.RemoteHost) ? null : request.RemoteHost.Trim();

        if (drive.RemoteHost is null ||
            string.Equals(drive.RemoteHost, requested, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var homeOnly = Drive.Create(
            drive.UserId,
            drive.Letter,
            drive.Host,
            drive.Name,
            drive.Username,
            drive.Password,
            protocol: drive.Protocol,
            port: drive.Port);

        return nasConnector.IsMountedFrom(drive) && !nasConnector.IsMountedFrom(homeOnly);
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

        if (!string.IsNullOrWhiteSpace(request.RemoteHost) && !GeneralValidation.IsValidHost(request.RemoteHost))
        {
            return Result.Failure(DriveErrors.InvalidRemoteHost);
        }

        return ProtocolValidation.Validate(
            request.Protocol,
            request.Port,
            request.Letter,
            request.Host,
            request.Name,
            request.Username,
            request.Password);
    }
}
