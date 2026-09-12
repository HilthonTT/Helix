using Helix.App.Models;

namespace Helix.App.Views.Drives;

public sealed partial class SidebarGroupTemplate : ContentView
{
    public SidebarGroupTemplate()
    {
        InitializeComponent();
    }

    private void Connect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: false);

    private void Disconnect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: true);

    private Task ToggleAsync(bool disconnect)
    {
        return BindingContext is DriveGroupDisplay group
            ? DriveGroupActions.ToggleAsync(group, disconnect)
            : Task.CompletedTask;
    }
}
