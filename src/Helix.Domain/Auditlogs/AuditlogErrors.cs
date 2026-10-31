namespace Helix.Domain.Auditlogs;

public static class AuditlogErrors
{
    public static readonly Error NothingToExport = Error.NotFound(
        "Auditlog.NothingToExport",
        "There is no history to export yet.");
}
