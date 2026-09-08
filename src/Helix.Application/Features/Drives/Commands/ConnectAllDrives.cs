using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ConnectAllDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public sealed record Request(bool OnlyAutoConnect = false);

    public async Task<Result> Handle(Request? request = null, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        bool onlyAutoConnect = request?.OnlyAutoConnect ?? false;

        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> connectedLetters = nasConnector.GetConnectedLetters();

        Drive[] disconnectedDrives = drives
            .Where(d => !DriveMountBatch.IsUp(d, connectedLetters, nasConnector))
            .Where(d => !onlyAutoConnect || d.AutoConnect)
            .ToArray();

        if (disconnectedDrives.Length == 0)
        {
            return Result.Success();
        }

        using IDisposable suppression = driveMonitor.Suppress(disconnectedDrives.Select(d => d.Letter));

        Result[] results = await Task.WhenAll(
            disconnectedDrives.Select(drive => nasConnector.ConnectAsync(drive, cancellationToken)));

        List<string> failures = [];
        bool anyConnected = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].IsFailure)
            {
                failures.Add($"{disconnectedDrives[i].Letter}: {results[i].Error.Description}");
                continue;
            }

            disconnectedDrives[i].MarkConnected(dateTimeProvider.UtcNow);
            anyConnected = true;
        }

        if (anyConnected)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToConnect(string.Join(Environment.NewLine, failures)));
    }
}
