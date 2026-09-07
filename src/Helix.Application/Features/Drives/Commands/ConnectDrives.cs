using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ConnectDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public sealed record Request(IReadOnlyList<Guid> DriveIds, bool Disconnect = false);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        if (request.DriveIds.Count == 0)
        {
            return Result.Success();
        }

        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<Guid, Drive> byId = drives.ToDictionary(drive => drive.Id);

        Drive[] targets = [.. request.DriveIds
            .Distinct()
            .Where(byId.ContainsKey)
            .Select(id => byId[id])];

        if (targets.Length == 0)
        {
            return Result.Success();
        }

        return await DriveMountBatch.RunAsync(
            targets,
            request.Disconnect,
            nasConnector,
            driveMonitor,
            unitOfWork,
            dateTimeProvider,
            cancellationToken);
    }
}
