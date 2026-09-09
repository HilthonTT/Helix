using Helix.Domain.Auditlogs;

namespace Helix.Application.Features.Auditlogs.Contracts;

/// <summary>
/// One slice of a user's history, with the total so the caller can tell whether
/// there is more behind it without asking for a page it does not need.
/// </summary>
public sealed record AuditlogPage(List<Auditlog> Items, int Skip, int TotalCount)
{
    public bool HasMore => Skip + Items.Count < TotalCount;
}
