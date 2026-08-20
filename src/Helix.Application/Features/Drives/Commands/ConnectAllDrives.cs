using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ConnectAllDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    /// <param name="OnlyAutoConnect">
    /// True for the unattended passes — the startup connect and the watchdog — which
    /// must respect each drive's own <see cref="Drive.AutoConnect"/> flag. False when
    /// the user pressed "connect all", which is an explicit instruction covering every
    /// drive on screen, including the ones held back from the automatic passes.
    /// </param>
    public sealed record Request(bool OnlyAutoConnect = false);

    public async Task<Result> Handle(Request? request = null, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        bool onlyAutoConnect = request?.OnlyAutoConnect ?? false;

        // Tracked rather than AsNoTracking: the drives that come up are stamped with the
        // time so the dashboard can report when each was last reachable.
        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return Result.Success();
        }

        // Enumerate mounted drives once rather than calling IsConnected (a full
        // DriveInfo.GetDrives() scan) once per drive.
        HashSet<string> connectedLetters = nasConnector.GetConnectedLetters();

        Drive[] disconnectedDrives = drives
            .Where(d => !connectedLetters.Contains(d.Letter))
            .Where(d => !onlyAutoConnect || d.AutoConnect)
            .ToArray();

        if (disconnectedDrives.Length == 0)
        {
            return Result.Success();
        }

        // ConnectAsync never throws — it returns Result.Failure for expected failures
        // (e.g. a bad password), so Task.WhenAll cannot fault and we aggregate the
        // per-drive outcomes instead of using exceptions for control flow.
        Result[] results = await Task.WhenAll(
            disconnectedDrives.Select(drive => nasConnector.ConnectAsync(drive, cancellationToken)));

        List<string> failures = [];
        bool anyConnected = false;

        // Stamped here on the calling thread rather than inside the parallel connects,
        // so the change tracker is only ever touched from one thread.
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

        // One save for the whole batch. The audit interceptor ignores a change that only
        // moves this timestamp, so connecting ten drives does not file ten "changed"
        // entries in the log.
        if (anyConnected)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToConnect(string.Join(Environment.NewLine, failures)));
    }
}
