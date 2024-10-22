namespace Helix.App.Helpers;

public static class StorageUsageHelper
{
    public static string GetStorageUsage(string letter, string notReadyText = "Drive not ready")
    {
        var driveInfo = new DriveInfo(letter);
        if (driveInfo.IsReady)
        {
            double totalSizeInTB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0 * 1024.0);
            double freeSizeInTB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0 * 1024.0);
            double usedSizeInTB = totalSizeInTB - freeSizeInTB;

            return $"{usedSizeInTB:F1}TB used of {totalSizeInTB:F1}TB";
        }
        else
        {
            return notReadyText;
        }
    }
}
