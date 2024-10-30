using Helix.Domain.Auditlogs;

namespace Helix.App.Modals.Auditlogs.Search;

internal sealed record AuditlogsSearchedMessage(List<Auditlog> Auditlogs);
