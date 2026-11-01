namespace Helix.App.Common;

public static class StorageUsageHelper
{
    private const double BytesToTB = 1.0 / (1024.0 * 1024.0 * 1024.0 * 1024.0);

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

    public static string FormatUsedOf(long usedBytes, long totalBytes, string usedOfFormat)
    {
        double total = totalBytes * BytesToTB;
        double used = Math.Max(0, usedBytes * BytesToTB);

        return string.Format(usedOfFormat, used.ToString("F1"), total.ToString("F1"));
    }
}
