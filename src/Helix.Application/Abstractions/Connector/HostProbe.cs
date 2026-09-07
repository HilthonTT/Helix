namespace Helix.Application.Abstractions.Connector;

public sealed record HostProbe(bool Reachable, int? OpenPort, string? AlternateSpelling);
