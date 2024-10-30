using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Drives.Search;

internal sealed class SearchDrivesMessage(bool value) : ValueChangedMessage<bool>(value);
