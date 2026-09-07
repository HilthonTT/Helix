namespace Helix.Application.Abstractions.Connector;

public interface IHostReachability
{
    Task<bool> IsReachableAsync(string host, CancellationToken cancellationToken = default);
}
