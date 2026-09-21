namespace Helix.Application.Abstractions.Connector;

public interface IWakeOnLan
{
    bool CanLookUp { get; }

    Task<bool> TryWakeAsync(string? macAddress, CancellationToken cancellationToken = default);

    Task<bool> WakeNowAsync(string? macAddress, CancellationToken cancellationToken = default);

    Task<string?> LookUpAsync(string host, CancellationToken cancellationToken = default);
}
