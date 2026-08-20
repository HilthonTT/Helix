namespace Helix.App.Common;

public static class StorageUsageHelper
{
    private const double BytesToTB = 1.0 / (1024.0 * 1024.0 * 1024.0 * 1024.0);

    /// <summary>
    /// How long a capacity probe may take before the caller gives up on it.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Off-thread <see cref="GetStorageUsage"/>. Reading <c>IsReady</c>, <c>TotalSize</c>
    /// or <c>AvailableFreeSpace</c> performs I/O against the share and blocks for as
    /// long as the network takes to fail, so once the monitor started refreshing rows
    /// on a timer these could no longer be called on the UI thread.
    /// </summary>
    public static Task<string> GetStorageUsageAsync(
        string driveLetter,
        string driveNotReadyMessage = "Drive not ready",
        string invalidDriveMessage = "Invalid drive letter")
    {
        return ProbeAsync(
            () => GetStorageUsage(driveLetter, driveNotReadyMessage, invalidDriveMessage),
            driveNotReadyMessage);
    }

    /// <summary>Off-thread <see cref="GetCompactUsage"/>.</summary>
    public static Task<string> GetCompactUsageAsync(string driveLetter, string fallback = "0 TB")
    {
        return ProbeAsync(() => GetCompactUsage(driveLetter, fallback), fallback);
    }

    /// <summary>
    /// Renders an already-totalled pair of byte counts as the compact "1.2 / 4.0 TB"
    /// form the dashboard tile uses.
    /// </summary>
    /// <remarks>
    /// Formatting only. Working out which drives to add up is deliberately not done
    /// here: several mapped drives are often shares of one NAS pool and each reports
    /// that pool's whole size, so adding letters together triple-counts a server mapped
    /// three times. Deciding what counts as one volume needs the platform, and lives
    /// behind <c>IStorageProbe</c> in Infrastructure.
    /// </remarks>
    public static string FormatCombined(long usedBytes, long totalBytes, string fallback = "0 TB")
    {
        if (totalBytes <= 0)
        {
            return fallback;
        }

        double total = totalBytes * BytesToTB;
        double used = Math.Max(0, usedBytes * BytesToTB);

        return $"{used:F1} / {total:F1} TB";
    }

    private static async Task<T> ProbeAsync<T>(Func<T> probe, T fallback)
    {
        try
        {
            // A blocking DriveInfo read cannot be cancelled, so the abandoned task is
            // left to finish on its own; what matters is that the caller is released.
            return await Task.Run(probe).WaitAsync(ProbeTimeout);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static string GetStorageUsage(
        string driveLetter, 
        string driveNotReadyMessage = "Drive not ready",
        string invalidDriveMessage = "Invalid drive letter")
    {
        try
        {
            var driveInfo = new DriveInfo(driveLetter);

            if (!driveInfo.IsReady)
            {
                return driveNotReadyMessage;
            }

            if (driveInfo.TotalSize == 0)
            {
                return "Drive size is zero.";
            }

            double totalSizeInTB = driveInfo.TotalSize * BytesToTB;
            double availableSpaceInTB = driveInfo.AvailableFreeSpace * BytesToTB;
            double usedSpaceInTB = totalSizeInTB - availableSpaceInTB;

            if (usedSpaceInTB < 0)
            {
                usedSpaceInTB = 0;
            }

            return $"{usedSpaceInTB:F1}TB used of {totalSizeInTB:F1}TB";
        }
        catch (IOException)
        {
            return invalidDriveMessage;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Short "1.2 / 4.0 TB" form for the dashboard stat tile, where the sentence-length
    /// <see cref="GetStorageUsage"/> string reads as a paragraph rather than a figure.
    /// </summary>
    public static string GetCompactUsage(string driveLetter, string fallback = "0 TB")
    {
        try
        {
            var driveInfo = new DriveInfo(driveLetter);

            if (!driveInfo.IsReady || driveInfo.TotalSize == 0)
            {
                return fallback;
            }

            double totalSizeInTB = driveInfo.TotalSize * BytesToTB;
            double usedSpaceInTB = Math.Max(0, totalSizeInTB - (driveInfo.AvailableFreeSpace * BytesToTB));

            return $"{usedSpaceInTB:F1} / {totalSizeInTB:F1} TB";
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
