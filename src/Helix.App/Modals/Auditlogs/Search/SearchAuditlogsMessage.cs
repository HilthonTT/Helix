using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Auditlogs.Search;

internal sealed class SearchAuditlogsMessage(bool value) : ValueChangedMessage<bool>(value);
