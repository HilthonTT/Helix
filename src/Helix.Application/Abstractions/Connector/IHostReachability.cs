namespace Helix.Application.Abstractions.Connector;

public interface IHostReachability
{
    Task<bool> IsReachableAsync(
        string host,
        CancellationToken cancellationToken = default,
        IReadOnlyList<int>? ports = null);

    Task<bool> ProbeNowAsync(
        string host,
        CancellationToken cancellationToken = default,
        IReadOnlyList<int>? ports = null);
}
