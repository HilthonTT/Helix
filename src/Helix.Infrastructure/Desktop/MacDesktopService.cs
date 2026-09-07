#if MACCATALYST
using Helix.Application.Abstractions.Desktop;
using Helix.Infrastructure.Platform;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Desktop;

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
