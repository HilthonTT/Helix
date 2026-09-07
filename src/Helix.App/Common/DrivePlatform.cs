namespace Helix.App.Common;

internal static class DrivePlatform
{
    public static bool SupportsPersistentMappings =>
#if WINDOWS
        true;
#else
        false;
#endif

    public static bool SupportsHostnameConnect =>
#if WINDOWS
        true;
#else
        false;
#endif
}
