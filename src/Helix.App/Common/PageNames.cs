namespace Helix.App.Common;

public static class PageNames
{
    public const string LoginPage = "login";

    public const string RegisterPage = "register";

    /// <summary>
    /// The idle lock screen.
    /// </summary>
    /// <remarks>
    /// A route of its own rather than a reuse of <see cref="LoginPage"/>: the session
    /// behind it is still live, and everything that acts as the signed-in user keeps
    /// running while it is up.
    /// </remarks>
    public const string LockPage = "lock";

    public const string HomePage = "dashboard";

    public const string SettingsPage = "settings";

    public const string AuditlogsPage = "auditlogs";
}

