namespace Helix.Domain.Drives;

public sealed record DriveFailure(string Letter, Error Error)
{
    public override string ToString() => $"{Letter}: {Error.Description}";
}
