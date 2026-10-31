using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Auditlogs;

namespace Helix.Application.Core.Drives;

internal static class ReliabilitySummary
{
    internal static DriveReliability Summarize(IEnumerable<Auditlog> history, DateTime utcNow, int windowDays)
    {
        DateTime weekAgo = utcNow.AddDays(-7);

        int drops = 0;
        int dropsLastWeek = 0;
        int failedReconnects = 0;
        DateTime? lastDrop = null;
        DateTime? pendingDrop = null;
        List<TimeSpan> recoveries = [];

        foreach (Auditlog row in history)
        {
            switch (row.Action)
            {
                case AuditAction.DriveDisconnected:
                    drops++;

                    if (row.CreatedOnUtc >= weekAgo)
                    {
                        dropsLastWeek++;
                    }

                    lastDrop = row.CreatedOnUtc;
                    pendingDrop = row.CreatedOnUtc;
                    break;

                case AuditAction.DriveReconnectFailed:
                    failedReconnects++;
                    break;

                case AuditAction.DriveReconnected when pendingDrop is DateTime droppedAt:
                    recoveries.Add(row.CreatedOnUtc - droppedAt);
                    pendingDrop = null;
                    break;
            }
        }

        return new DriveReliability(
            windowDays,
            drops,
            dropsLastWeek,
            recoveries.Count,
            failedReconnects,
            Median(recoveries),
            lastDrop);
    }

    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();

        int middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }
}
