using Helix.Application.Abstractions.Desktop;
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
            CreateDesktopShortcut();
        else
            DeleteDesktopShortcut();
    }

    private static void CreateDesktopShortcut()
    {
        Ensure.NotNull(DesktopFolder, nameof(DesktopFolder));

        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
            throw new InvalidOperationException("The process path is null or invalid.");

        // Late binding - no COM reference needed at compile time
        try
        {
            // Create WScript.Shell dynamically
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("WScript.Shell is not available.");

            object? shellObj = Activator.CreateInstance(shellType);
            if (shellObj == null)
                throw new InvalidOperationException("Failed to create WScript.Shell instance.");

            try
            {
                dynamic shell = shellObj;
                dynamic shortcut = shell.CreateShortcut(ShortcutPath);

                shortcut.TargetPath = processPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(processPath);
                shortcut.Description = "Helix desktop shortcut.";
                // Optional: shortcut.IconLocation = "...";

                shortcut.Save();
            }
            finally
            {
                if (shellObj is IDisposable disposable)
                    disposable.Dispose();
                else
                    Marshal.ReleaseComObject(shellObj);
            }
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
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to delete desktop shortcut.", ex);
        }
    }
}
