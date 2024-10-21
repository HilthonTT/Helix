using Helix.Domain.Drives;
using SharedKernel;

namespace Helix.Application.Abstractions.Connector;

public interface INasConnector
{
    Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default);

    bool IsConnected(string letter);
}
