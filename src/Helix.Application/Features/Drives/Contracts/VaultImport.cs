using Helix.Domain.Drives;

namespace Helix.Application.Features.Drives.Contracts;

public sealed record VaultImport(List<Drive> Drives, int GroupCount, int ScheduleCount);
