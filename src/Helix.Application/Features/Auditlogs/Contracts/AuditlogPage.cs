using Helix.Domain.Auditlogs;

namespace Helix.Application.Features.Auditlogs.Contracts;

public sealed record AuditlogPage(List<Auditlog> Items, int Skip, int TotalCount)
{
    public bool HasMore => Skip + Items.Count < TotalCount;
}
