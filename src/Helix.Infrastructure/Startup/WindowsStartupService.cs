using Helix.Application.Abstractions.Startup;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Startup;

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupService : IStartupService
{
    private const string ShortcutName = "Helix.lnk";

    private static readonly string StartupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static readonly string ShortcutPath = Path.Combine(StartupFolder, ShortcutName);

    public bool IsSetToStartup()
    {
        return File.Exists(ShortcutPath);
    }

    public void ToggleStartup(bool value)
    {
        if (value)
        {
            CreateStartupShortcut();
        }
        else
        {
            DeleteShortcut();
        }
    }

    private static void CreateStartupShortcut()
    {
        Ensure.NotNull(StartupFolder, nameof(StartupFolder));

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            throw new InvalidOperationException("The process path is null or invalid.");

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("WScript.Shell is not available on this system.");

        object? shellObj = null;

        try
        {
            shellObj = Activator.CreateInstance(shellType);
            if (shellObj == null)
                throw new InvalidOperationException("Failed to create WScript.Shell instance.");

            dynamic shell = shellObj;
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);

            shortcut.TargetPath = processPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(processPath)
                ?? throw new InvalidOperationException("Unable to determine the working directory.");
            shortcut.Description = "Helix startup shortcut.";

            shortcut.Save();
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to create startup shortcut.", ex);
        }
        finally
        {
            if (shellObj != null)
            {
                try { Marshal.ReleaseComObject(shellObj); } catch { }
            }
        }
    }

    private static void DeleteShortcut()
    {
        try
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to delete the startup shortcut.", ex);
        }
    }
}
