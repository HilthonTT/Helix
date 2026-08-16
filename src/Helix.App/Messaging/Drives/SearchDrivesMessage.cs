using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Drives;

internal sealed class SearchDrivesMessage(bool value) : ValueChangedMessage<bool>(value);
