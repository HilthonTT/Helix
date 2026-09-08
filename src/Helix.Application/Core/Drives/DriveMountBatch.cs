using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Time;
using Helix.Domain.Drives;

namespace Helix.Application.Core.Drives;

internal static class DriveMountBatch
{
    public static bool IsUp(Drive drive, HashSet<string> connectedLetters, INasConnector nasConnector) =>
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

        Drive[] targets = [.. drives.Where(drive => IsUp(drive, connected, nasConnector) == disconnect)];
        if (targets.Length == 0)
        {
            return Result.Success();
        }

        using IDisposable suppression = driveMonitor.Suppress(targets.Select(drive => drive.Letter));

        Result[] results = await Task.WhenAll(targets.Select(drive => disconnect
            ? nasConnector.DisconnectAsync(drive, cancellationToken)
            : nasConnector.ConnectAsync(drive, cancellationToken)));

        List<string> failures = [];
        bool anyConnected = false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].IsFailure)
            {
                failures.Add($"{targets[i].Letter}: {results[i].Error.Description}");
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

        string detail = string.Join(Environment.NewLine, failures);

        return Result.Failure(disconnect
            ? DriveErrors.FailedToDisconnect(detail)
            : DriveErrors.FailedToConnect(detail));
    }
}
