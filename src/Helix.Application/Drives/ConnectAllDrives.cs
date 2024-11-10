using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class ConnectAllDrives(
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

        List<Drive> disconnectedDrives = drives
            .Where(d => !nasConnector.IsConnected(d.Letter))
            .ToList();

        IEnumerable<Task> connectTasks = disconnectedDrives.Select(async drive =>
        {
            Result result = await nasConnector.ConnectAsync(drive, cancellationToken);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(result.Error.Description);
            }
        });

        try
        {
            await Task.WhenAll(connectTasks);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        return Result.Success();
    }
}
