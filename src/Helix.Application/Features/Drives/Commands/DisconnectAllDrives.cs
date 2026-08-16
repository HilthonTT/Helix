using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class DisconnectAllDrives(
    IDriveRepository driveRepository, 
    ILoggedInUser loggedInUser, 
    INasConnector nasConnector) : IHandler
{
    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Success();
        }

        // Enumerate mounted drives once rather than calling IsConnected (a full
        // DriveInfo.GetDrives() scan) once per drive.
        HashSet<string> connectedLetters = nasConnector.GetConnectedLetters();

        Drive[] connectedDrives = drives
            .Where(d => connectedLetters.Contains(d.Letter))
            .ToArray();

        if (connectedDrives.Length == 0)
        {
            return Result.Success();
        }

        // DisconnectAsync never throws — it returns Result.Failure for expected
        // failures, so we aggregate the per-drive outcomes instead of using
        // exceptions for control flow.
        Result[] results = await Task.WhenAll(
            connectedDrives.Select(drive => nasConnector.DisconnectAsync(drive, cancellationToken)));

        List<string> failures = [];
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].IsFailure)
            {
                failures.Add($"{connectedDrives[i].Letter}: {results[i].Error.Description}");
            }
        }

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(string.Join(Environment.NewLine, failures)));
    }
}
