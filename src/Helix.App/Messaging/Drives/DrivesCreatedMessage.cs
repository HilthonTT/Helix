using Helix.Domain.Drives;

namespace Helix.App.Messaging.Drives;

public sealed record DrivesCreatedMessage(IReadOnlyList<Drive> Drives);
