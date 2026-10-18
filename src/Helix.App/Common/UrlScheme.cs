using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace Helix.App.Common;

internal static class UrlScheme
{
    public static void Register(ILogger logger)
    {
#if WINDOWS
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            string command = $"\"{executable}\" \"%1\"";

            using RegistryKey scheme = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{CommandRequest.Scheme}");
            using RegistryKey open = scheme.CreateSubKey(@"shell\open\command");

            if (open.GetValue(null) as string == command)
            {
                return;
            }

            scheme.SetValue(null, "URL:Helix");
            scheme.SetValue("URL Protocol", string.Empty);

            using (RegistryKey icon = scheme.CreateSubKey("DefaultIcon"))
            {
                icon.SetValue(null, $"\"{executable}\",0");
            }

            open.SetValue(null, command);

            logger.LogInformation("Registered the {Scheme}:// link scheme for this user.", CommandRequest.Scheme);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not register the {Scheme}:// link scheme; links will not open Helix.", CommandRequest.Scheme);
        }
#endif
    }
}
