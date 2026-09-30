#if WINDOWS
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Desktop;

[SupportedOSPlatform("windows")]
public static class ShellIcons
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public const string ColorSetChanged = "ImmersiveColorSet";

    public static bool TaskbarIsLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string? CurrentPath()
    {
        string file = TaskbarIsLight() ? "helix-light-taskbar.ico" : "helix-dark-taskbar.ico";

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, InfrastructureAssembly.Instance.GetName().Name ?? string.Empty, "Icons", file),
            Path.Combine(AppContext.BaseDirectory, "Icons", file)
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
#endif
