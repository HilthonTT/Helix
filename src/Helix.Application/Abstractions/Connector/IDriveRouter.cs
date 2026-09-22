using Helix.Domain.Drives;

namespace Helix.Application.Abstractions.Connector;

public sealed record DriveRoute(string Host, bool IsRemote);

public interface IDriveRouter
{
    Task<Result<DriveRoute>> RouteAsync(Drive drive, bool fresh = false, CancellationToken cancellationToken = default);
}
