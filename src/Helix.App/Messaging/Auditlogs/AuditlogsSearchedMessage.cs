using Helix.Domain.Auditlogs;

namespace Helix.App.Messaging.Auditlogs;

internal sealed record AuditlogsSearchedMessage(List<Auditlog> Auditlogs);
