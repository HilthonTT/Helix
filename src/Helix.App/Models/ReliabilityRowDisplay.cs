using Helix.Application.Features.Drives.Contracts;

namespace Helix.App.Models;

public sealed class ReliabilityRowDisplay(DriveReliabilityEntry entry)
{
    private readonly DriveReliabilityDisplay _reliability = new(entry.Reliability);

    public string Letter => $"{entry.Letter}:";

    public string Name => entry.Name;

    public string Drops => _reliability.Drops;

    public string Recovery => _reliability.Recovery;

    public bool HasRecovery => _reliability.HasRecovery;

    public bool IsFlaky => _reliability.IsFlaky;

    public bool HasDropped => entry.Reliability.HasDropped;
}
