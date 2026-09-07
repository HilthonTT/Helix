namespace Helix.Application.Abstractions.Connector;

public interface IHostReachability
{
    Task<bool> IsReachableAsync(string host, CancellationToken cancellationToken = default);

    Task<bool> ProbeNowAsync(string host, CancellationToken cancellationToken = default);
}
