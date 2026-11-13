namespace Helix.Application.Features.Drives.Contracts;

public sealed record DiscoveredServer(
    string Address,
    string DisplayName,
    string? Model,
    IReadOnlyList<string> ExistingLetters)
{
    public bool AlreadyAdded => ExistingLetters.Count > 0;
}
