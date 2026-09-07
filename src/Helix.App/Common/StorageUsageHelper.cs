namespace Helix.App.Common;

public static class StorageUsageHelper
{
    private const double BytesToTB = 1.0 / (1024.0 * 1024.0 * 1024.0 * 1024.0);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public static Task<string> GetStorageUsageAsync(
        string driveLetter,
        string driveNotReadyMessage = "Drive not ready",
        string invalidDriveMessage = "Invalid drive letter",
        string usedOfFormat = DefaultUsedOfFormat)
    {
        return ProbeAsync(
            () => GetStorageUsage(driveLetter, driveNotReadyMessage, invalidDriveMessage, usedOfFormat),
            driveNotReadyMessage);
    }

    private const string DefaultUsedOfFormat = "{0} TB used of {1} TB";

    public static Task<string> GetCompactUsageAsync(string driveLetter, string fallback = "0 TB")
    {
        return ProbeAsync(() => GetCompactUsage(driveLetter, fallback), fallback);
    }

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
        string invalidDriveMessage = "Invalid drive letter",
        string usedOfFormat = DefaultUsedOfFormat)
    {
        try
        {
            var driveInfo = new DriveInfo(driveLetter);

            if (!driveInfo.IsReady || driveInfo.TotalSize == 0)
            {
                return driveNotReadyMessage;
            }

            double totalSizeInTB = driveInfo.TotalSize * BytesToTB;
            double availableSpaceInTB = driveInfo.AvailableFreeSpace * BytesToTB;
            double usedSpaceInTB = Math.Max(0, totalSizeInTB - availableSpaceInTB);

            return string.Format(usedOfFormat, usedSpaceInTB.ToString("F1"), totalSizeInTB.ToString("F1"));
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
