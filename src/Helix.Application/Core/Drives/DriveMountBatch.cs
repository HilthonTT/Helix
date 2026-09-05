using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Time;
using Helix.Domain.Drives;

namespace Helix.Application.Core.Drives;

/// <summary>
/// Mounts or unmounts a set of drives the caller has already resolved and checked.
/// </summary>
/// <remarks>
/// Two use cases hand a list of drives to the same piece of work — a drive group, and an
/// ad-hoc selection of rows — and everything from here down is identical between them:
/// which of the set are in the wrong state, one suppression covering the whole batch, the
/// mounts in parallel, the stamps on one thread, the failures aggregated into a single
/// error. Only how the list was arrived at differs, so only that lives in the handlers.
///
/// It is a static helper rather than a handler because it does no authorization and no
/// lookup — the caller has already established that these drives exist and belong to the
/// signed-in user, and this must not be reachable by anything that has not.
/// </remarks>
internal static class DriveMountBatch
{
    /// <param name="drives">
    /// The drives to act on, owned and in the order failures should be reported in.
    /// Tracked entities when <paramref name="disconnect"/> is false, since the ones that
    /// come up are stamped.
    /// </param>
    /// <param name="disconnect">False mounts the set, true takes it down.</param>
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

        // Enumerated once rather than asking IsConnected per drive, which is a full
        // logical-drive scan each time.
        HashSet<string> connected = nasConnector.GetConnectedLetters();

        Drive[] targets = [.. drives.Where(drive => connected.Contains(drive.Letter) == disconnect)];
        if (targets.Length == 0)
        {
            return Result.Success();
        }

        // Helix changing these letters on purpose, in both directions, so the monitor is
        // told to expect it — otherwise taking a set down is followed by a tray toast per
        // drive and the watchdog putting every one of them back.
        using IDisposable suppression = driveMonitor.Suppress(targets.Select(drive => drive.Letter));

        // Neither call throws for an expected failure, so WhenAll cannot fault and the
        // per-drive outcomes are aggregated rather than thrown.
        Result[] results = await Task.WhenAll(targets.Select(drive => disconnect
            ? nasConnector.DisconnectAsync(drive, cancellationToken)
            : nasConnector.ConnectAsync(drive, cancellationToken)));

        List<string> failures = [];
        bool anyConnected = false;

        // Stamped on this thread rather than inside the parallel work, so the change
        // tracker is only ever touched from one.
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
