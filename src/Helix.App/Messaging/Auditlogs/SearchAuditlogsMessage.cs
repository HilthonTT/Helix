using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Auditlogs;

internal sealed class SearchAuditlogsMessage(bool value) : ValueChangedMessage<bool>(value);
