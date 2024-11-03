using Helix.Application.Abstractions.Startup;
using IWshRuntimeLibrary;
using SharedKernel;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Startup;

internal sealed class StartupService : IStartupService
{
    private const string ShortcutName = "Helix.lnk";

    private static readonly string StartupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static readonly string ShortcutPath = Path.Combine(StartupFolder, ShortcutName);

    public bool IsSetToStartup()
    {
        return System.IO.File.Exists(ShortcutPath);
    }

    public void ToggleStartup(bool value)
    {
        if (value)
        {
            ToggleStartupInternal();
        }
        else
        {
            DeleteShortcut();
        }
    }

    private static void ToggleStartupInternal()
    {
        // Check if the Startup folder path is accessible
        Ensure.NotNull(StartupFolder, nameof(StartupFolder));

        // Ensure the process path is not null or invalid before proceeding
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
            shortcut.Description = "Helix startup shortcut.";

            shortcut.Save();
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to create or save the startup shortcut.", ex);
        }
        finally
        {
            if (shellObj is not null)
            {
                Marshal.ReleaseComObject(shellObj);
            }
        }
    }

    private static void DeleteShortcut()
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
            throw new IOException("Failed to delete the startup shortcut.", ex);
        }
    }
}
