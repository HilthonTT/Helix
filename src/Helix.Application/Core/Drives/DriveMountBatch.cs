using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Time;
using Helix.Domain.Drives;

namespace Helix.Application.Core.Drives;

internal static class DriveMountBatch
{
    public static bool IsUp(Drive drive, HashSet<string> connectedLetters, INasConnector nasConnector) =>
        connectedLetters.Contains(drive.Letter) && nasConnector.IsLiveFrom(drive);

    public static bool IsMapped(Drive drive, HashSet<string> connectedLetters, INasConnector nasConnector) =>
        connectedLetters.Contains(drive.Letter) && nasConnector.IsMountedFrom(drive);

    public static async Task<Result> RunAsync(
        IReadOnlyList<Drive> drives,
        bool disconnect,
        INasConnector nasConnector,
        IDriveMonitor driveMonitor,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        if (drives.Count == 0)
        {
            return Result.Success();
        }

        HashSet<string> connected = nasConnector.GetConnectedLetters();

        Drive[] targets = [.. drives.Where(drive => disconnect
            ? IsMapped(drive, connected, nasConnector)
            : !IsUp(drive, connected, nasConnector))];
        if (targets.Length == 0)
        {
            return Result.Success();
        }

        using IDisposable suppression = driveMonitor.Suppress(targets.Select(drive => drive.Letter));

        Result[] results = await Task.WhenAll(targets.Select(drive => disconnect
            ? nasConnector.DisconnectAsync(drive, cancellationToken)
            : nasConnector.ConnectAsync(drive, cancellationToken)));

        List<DriveFailure> failures = [];
        bool anyConnected = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].IsFailure && (disconnect || !SettledLate(targets[i], results[i], nasConnector)))
            {
                failures.Add(new DriveFailure(targets[i].Letter, results[i].Error));
                continue;
            }

            if (!disconnect)
            {
                targets[i].MarkConnected(dateTimeProvider.UtcNow);
                anyConnected = true;
            }
        }

        if (anyConnected)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (failures.Count == 0)
        {
            return Result.Success();
        }

        return Result.Failure(disconnect
            ? DriveErrors.FailedToDisconnect(failures)
            : DriveErrors.FailedToConnect(failures));
    }

    public static bool SettledLate(Drive drive, Result result, INasConnector nasConnector) =>
        result.Error == DriveErrors.ConnectionTimedOut &&
        nasConnector.IsLiveFrom(drive);
}
