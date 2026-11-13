namespace Helix.Application.Abstractions.Connector;

public sealed record DiscoveredHost(string Address, string? Name, string? HostName, string? Model);
