namespace Helix.App.Common;

/// <summary>
/// What the running head can actually do with a drive, for the parts of the UI that
/// have to hide rather than disable an option.
/// </summary>
/// <remarks>
/// The two modals share their XAML shape across both heads, so the difference has to be
/// something they can bind to. Kept here rather than duplicated behind <c>#if</c> in
/// each viewmodel — there is one answer per platform, not one per screen.
/// </remarks>
internal static class DrivePlatform
{
    /// <summary>
    /// Whether a mapping can be written into the OS so it survives Helix being closed.
    /// Windows does this through <c>CONNECT_UPDATE_PROFILE</c>; macOS has no equivalent
    /// for a NetFS mount, so the option is not offered there.
    /// </summary>
    public static bool SupportsPersistentMappings =>
#if WINDOWS
        true;
#else
        false;
#endif
}
