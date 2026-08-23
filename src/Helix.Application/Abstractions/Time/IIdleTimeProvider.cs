namespace Helix.Application.Abstractions.Time;

/// <summary>
/// How long the machine has gone without input from the person using it.
/// </summary>
/// <remarks>
/// System-wide rather than app-wide, deliberately: someone working in another window is
/// at their desk, and locking Helix behind their back because they have not clicked on it
/// for ten minutes would be an app that interrupts to no purpose. What the lock is for is
/// the machine nobody is sitting at.
/// </remarks>
public interface IIdleTimeProvider
{
    /// <summary>Whether this platform can answer at all.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Time since the last keyboard or pointer input.
    /// </summary>
    /// <remarks>
    /// Answers <see cref="TimeSpan.Zero"/> — "someone is here" — whenever it cannot tell,
    /// because the alternative is locking a machine that is in use, and a lock the user
    /// did not ask for is far more annoying than one that failed to happen.
    /// </remarks>
    TimeSpan GetIdleTime();
}
