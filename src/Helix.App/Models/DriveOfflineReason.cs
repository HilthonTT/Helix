namespace Helix.App.Models;

/// <summary>
/// Why a drive is not mounted, as far as the row can tell.
/// </summary>
/// <remarks>
/// The row used to have one word for every drive that was not connected, which is the
/// state a laptop away from its NAS is in all day: thirteen red "Disconnected" pills, one
/// per share, indistinguishable from thirteen wrong passwords. The domain has drawn this
/// distinction since <see cref="Domain.Drives.DriveErrors.HostUnreachable"/> — the
/// watchdog reads it to pick a flat retry over an escalating backoff — and this carries it
/// the last step to the screen.
///
/// It is only ever set from an attempt that actually happened. A drive nobody has tried to
/// mount stays <see cref="Unknown"/> and shows the plain pill, rather than being guessed at.
/// </remarks>
internal enum DriveOfflineReason
{
    /// <summary>
    /// Not connected, and nothing has been attempted — the resting state of a drive the
    /// user disconnected on purpose, or has not connected yet.
    /// </summary>
    Unknown,

    /// <summary>The NAS did not answer, so the share was never asked.</summary>
    HostUnreachable,

    /// <summary>The NAS answered and the mount failed anyway.</summary>
    Refused
}
