using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Drives;

internal sealed class AdoptMappingsMessage(bool value) : ValueChangedMessage<bool>(value);
