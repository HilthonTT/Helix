#if MACCATALYST
using Foundation;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Platform;

[SupportedOSPlatform("maccatalyst")]
internal static class MacBundle
{
    public static string BundlePath =>
        NSBundle.MainBundle.BundlePath
        ?? throw new InvalidOperationException("The application bundle path could not be determined.");

    public static string BundleName =>
        Path.GetFileNameWithoutExtension(BundlePath.TrimEnd(Path.DirectorySeparatorChar));
}
#endif
