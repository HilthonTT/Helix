using Helix.Application.Abstractions.Desktop;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Helix.Infrastructure.Desktop;

/// <summary>
/// Opens a folder through the operating system's shell — Explorer on Windows, Finder on
/// macOS.
/// </summary>
/// <remarks>
/// One implementation for both heads, which is why it is registered outside
/// <c>AddPlatformServices</c>. <see cref="ProcessStartInfo.UseShellExecute"/> is what does
/// the work: on Windows it hands the path to the shell the same way a double-click would,
/// and on macOS .NET routes it through <c>open</c>. Neither needs a path built by hand or
/// a per-OS branch.
/// </remarks>
internal sealed class FileBrowser(ILogger<FileBrowser> logger) : IFileBrowser
{
    public Result Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            // The drive was mounted when the row was drawn and is not now. Nothing has
            // gone wrong beyond the user being a moment late.
            return Result.Failure(DriveErrors.MountNotAvailable);
        }

        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            return Result.Success();
        }
        catch (Exception ex)
        {
            // The path is deliberately not logged: on macOS it carries the user's home
            // directory, and the log is a file the user is asked to send to a stranger.
            logger.LogWarning(ex, "Could not open a mount in the file manager.");

            return Result.Failure(DriveErrors.MountNotOpened(ex.Message));
        }
    }
}
