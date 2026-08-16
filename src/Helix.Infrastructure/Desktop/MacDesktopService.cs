#if MACCATALYST
using Helix.Application.Abstractions.Desktop;
using Helix.Infrastructure.Platform;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Desktop;

/// <summary>
/// Puts a link to the app bundle on the Desktop — the macOS counterpart of the Windows
/// .lnk shortcut.
/// </summary>
/// <remarks>
/// A symlink rather than a Finder alias: aliases are an opaque resource-fork format
/// that only Cocoa's bookmark APIs can write, whereas a symlink to a <c>.app</c> is
/// launched by Finder exactly like the real thing and can be created and removed with
/// plain file APIs.
/// </remarks>
[SupportedOSPlatform("maccatalyst")]
internal sealed class MacDesktopService : IDesktopService
{
    private static readonly string DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    public void ToggleDesktopShortcut(bool value)
    {
        if (value)
        {
            CreateDesktopShortcut();
        }
        else
        {
            DeleteDesktopShortcut();
        }
    }

    private static string ShortcutPath => Path.Combine(DesktopFolder, $"{MacBundle.BundleName}.app");

    private static void CreateDesktopShortcut()
    {
        string bundlePath = MacBundle.BundlePath;

        if (!Directory.Exists(bundlePath))
        {
            throw new InvalidOperationException("The application bundle could not be found.");
        }

        try
        {
            DeleteDesktopShortcut();

            Directory.CreateSymbolicLink(ShortcutPath, bundlePath);
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to create desktop shortcut.", ex);
        }
    }

    private static void DeleteDesktopShortcut()
    {
        try
        {
            // Only ever remove our own symlink. Resolving the link target first means a
            // real folder that happens to share the name is left alone.
            var link = new DirectoryInfo(ShortcutPath);

            if (link.Exists && link.LinkTarget is not null)
            {
                link.Delete();
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to delete desktop shortcut.", ex);
        }
    }
}
#endif
