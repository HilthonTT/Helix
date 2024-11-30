using Helix.Domain.Drives;

namespace Helix.App.Messages;

public sealed record FillDrivesMessage(List<Drive> Drives);
