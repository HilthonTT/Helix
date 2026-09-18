namespace Helix.Application.Features.Drives.Contracts;

public sealed record AvailableShare(string Name, string? ExistingLetter)
{
    public bool AlreadyAdded => ExistingLetter is not null;
}
