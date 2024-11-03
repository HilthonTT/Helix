using Helix.Application.Abstractions.Desktop;
using IWshRuntimeLibrary;
using SharedKernel;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Desktop;

internal sealed class DesktopService : IDesktopService
{
    private const string ShortcutName = "Helix.lnk";
    private static readonly string DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private static readonly string ShortcutPath = Path.Combine(DesktopFolder, ShortcutName);

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

    private static void CreateDesktopShortcut()
    {
        // Ensure the Desktop folder path is accessible
        Ensure.NotNull(DesktopFolder, nameof(DesktopFolder));

        // Ensure the process path is valid
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !System.IO.File.Exists(processPath))
        {
            throw new InvalidOperationException("The process path is null or invalid.");
        }

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        Ensure.NotNull(shellType, nameof(shellType));

        object? shellObj = null;
        try
        {
            shellObj = Activator.CreateInstance(shellType);
            if (shellObj is not IWshShell shell)
            {
                throw new InvalidOperationException("Failed to create WScript.Shell instance.");
            }

            // Create the shortcut
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = processPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(processPath)
                ?? throw new InvalidOperationException("Unable to determine the working directory.");
            shortcut.Description = "Helix desktop shortcut.";

            shortcut.Save();
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to create or save the desktop shortcut.", ex);
        }
        finally
        {
            if (shellObj is not null)
            {
                Marshal.ReleaseComObject(shellObj);
            }
        }
    }

    private static void DeleteDesktopShortcut()
    {
        try
        {
            if (System.IO.File.Exists(ShortcutPath))
            {
                System.IO.File.Delete(ShortcutPath);
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to delete the desktop shortcut.", ex);
        }
    }
}
