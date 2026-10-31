namespace Helix.Application.Features.Drives.Contracts;

public sealed record DriveReliabilityEntry(
    Guid DriveId,
    string Letter,
    string Name,
    DriveReliability Reliability);
