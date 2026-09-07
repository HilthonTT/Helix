using Helix.Application.Abstractions.Desktop;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Helix.Infrastructure.Desktop;

internal sealed class FileBrowser(ILogger<FileBrowser> logger) : IFileBrowser
{
    public Result Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Result.Failure(DriveErrors.MountNotAvailable);
        }

        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open a mount in the file manager.");

            return Result.Failure(DriveErrors.MountNotOpened(ex.Message));
        }
    }
}
