namespace Helix.Application.Abstractions.Connector;

public interface IHostDiagnostics
{
    Task<HostProbe> ProbeAsync(string host, CancellationToken cancellationToken = default);
}
