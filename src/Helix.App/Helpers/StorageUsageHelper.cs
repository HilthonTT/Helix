namespace Helix.App.Helpers;

public static class StorageUsageHelper
{
    private const double BytesToTB = 1.0 / (1024.0 * 1024.0 * 1024.0 * 1024.0);

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
