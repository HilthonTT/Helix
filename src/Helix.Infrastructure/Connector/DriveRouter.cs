using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Connector;

internal sealed class DriveRouter(
    IHostReachability hostReachability,
    INetworkLocation networkLocation,
    IWakeOnLan wakeOnLan,
    ILogger<DriveRouter> logger) : IDriveRouter
{
    public async Task<Result<DriveRoute>> RouteAsync(
        Drive drive,
        bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        bool away = await IsAwayAsync(drive, cancellationToken);

        Task<bool>? remote = null;

        if (drive.RemoteHost is null || !away)
        {
            Task<bool> home = IsReachableAsync(drive.Host, fresh, cancellationToken);

            if (drive.RemoteHost is not null)
            {
                remote = IsReachableAsync(drive.RemoteHost, fresh, cancellationToken);
            }

            if (await home)
            {
                return new DriveRoute(drive.Host, IsRemote: false);
            }

            if (!away)
            {
                await wakeOnLan.TryWakeAsync(drive.MacAddress, cancellationToken);
            }

            if (drive.RemoteHost is null)
            {
                return Result.Failure<DriveRoute>(DriveErrors.HostUnreachable(drive.Host));
            }
        }

        if (await (remote ?? IsReachableAsync(drive.RemoteHost, fresh, cancellationToken)))
        {
            logger.LogDebug("Drive {Letter}: using its address for away from home.", drive.Letter);

            return new DriveRoute(drive.RemoteHost, IsRemote: true);
        }

        return Result.Failure<DriveRoute>(DriveErrors.HostUnreachable(away ? drive.RemoteHost : drive.Host));
    }

    private async Task<bool> IsAwayAsync(Drive drive, CancellationToken cancellationToken)
    {
        if (drive.HomeNetworkId is null || !networkLocation.IsSupported)
        {
            return false;
        }

        NetworkLocation? here = await networkLocation.GetCurrentAsync(cancellationToken);

        return drive.IsAwayFrom(here?.Id);
    }

    private Task<bool> IsReachableAsync(string host, bool fresh, CancellationToken cancellationToken) =>
        fresh
            ? hostReachability.ProbeNowAsync(host, cancellationToken)
            : hostReachability.IsReachableAsync(host, cancellationToken);
}
