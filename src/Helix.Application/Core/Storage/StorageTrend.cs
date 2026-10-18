namespace Helix.Application.Core.Storage;

public static class StorageTrend
{
    public const int MinimumSpanDays = 7;

    public const int MinimumSamples = 3;

    public const int HorizonDays = 3650;

    public static double? BytesPerDay(IReadOnlyList<(DateOnly Day, long FreeBytes)> samples)
    {
        if (samples.Count < MinimumSamples)
        {
            return null;
        }

        int first = samples.Min(s => s.Day.DayNumber);
        int last = samples.Max(s => s.Day.DayNumber);

        if (last - first < MinimumSpanDays)
        {
            return null;
        }

        double meanX = samples.Average(s => (double)(s.Day.DayNumber - first));
        double meanY = samples.Average(s => (double)s.FreeBytes);

        double covariance = 0;
        double variance = 0;

        foreach ((DateOnly day, long freeBytes) in samples)
        {
            double dx = day.DayNumber - first - meanX;

            covariance += dx * (freeBytes - meanY);
            variance += dx * dx;
        }

        return variance == 0 ? null : covariance / variance;
    }

    public static int? DaysUntilFull(long freeBytes, double? bytesPerDay)
    {
        if (bytesPerDay is not double rate || rate >= 0)
        {
            return null;
        }

        double days = freeBytes / -rate;

        return days > HorizonDays ? null : (int)Math.Floor(days);
    }
}
