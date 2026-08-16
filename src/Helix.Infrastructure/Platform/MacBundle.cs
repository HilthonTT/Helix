#if MACCATALYST
using Foundation;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Platform;

/// <summary>
/// Locates the running <c>.app</c> bundle, which is what both the login item and the
/// desktop alias have to point at.
/// </summary>
/// <remarks>
/// <c>Environment.ProcessPath</c> resolves to the inner Mach-O binary
/// (<c>Helix.app/Contents/MacOS/Helix</c>); handing that to <c>open</c> or to Finder
/// launches a headless process instead of the app. NSBundle knows the real answer.
/// </remarks>
[SupportedOSPlatform("maccatalyst")]
internal static class MacBundle
{
    /// <summary>Absolute path of the <c>.app</c> bundle directory.</summary>
    public static string BundlePath =>
        NSBundle.MainBundle.BundlePath
        ?? throw new InvalidOperationException("The application bundle path could not be determined.");

    /// <summary>The bundle name without its extension, used to name the desktop link.</summary>
    public static string BundleName =>
        Path.GetFileNameWithoutExtension(BundlePath.TrimEnd(Path.DirectorySeparatorChar));
}
#endif
