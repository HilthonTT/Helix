using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.Application.Features.DriveGroups.Commands;
using Microsoft.Extensions.Logging;
using Helix.App.Services;

namespace Helix.App.Views.Drives;

public sealed partial class DriveGroupTemplate : ContentView
{
    public DriveGroupTemplate()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
    }

    private void Connect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: false);

    private void Disconnect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: true);

    private async Task ToggleAsync(bool disconnect)
    {
        if (BindingContext is not DriveGroupDisplay group || group.IsBusy)
        {
            return;
        }

        try
        {
            group.IsBusy = true;

            var request = new ConnectDriveGroup.Request(group.Id, disconnect);

            Result result = await ScopedHandler.HandleAsync((ConnectDriveGroup h) => h.Handle(request));

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (Guid driveId in group.DriveIds)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(driveId));
            }

            if (result.IsFailure)
            {
                Notifier.Error(result.Error);
            }
        }
        catch (Exception ex)
        {
            AppLog.For<DriveGroupTemplate>().LogError(ex, "A drive group action failed.");
        }
        finally
        {
            group.IsBusy = false;
        }
    }
}
