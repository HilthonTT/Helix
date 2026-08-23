using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.Application.Features.DriveGroups.Commands;
using Microsoft.Extensions.Logging;

namespace Helix.App.Views.Drives;

public sealed partial class DriveGroupTemplate : ContentView
{
    public DriveGroupTemplate()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        // Without the base call the BindingContext is never propagated to Content,
        // leaving every {Binding} in the template unresolved.
        base.OnBindingContextChanged();
    }

    private void Connect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: false);

    private void Disconnect(object? sender, TappedEventArgs e) => _ = ToggleAsync(disconnect: true);

    /// <summary>
    /// Connects or disconnects the whole group, then tells the dashboard to catch up.
    /// </summary>
    /// <remarks>
    /// The failure of one drive does not hide the success of the others: the handler
    /// mounts what it can and reports the rest, so the alert lists the drives that
    /// refused rather than replacing the whole action with an error.
    /// </remarks>
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

            // Sent either way: whatever did work has changed the drive list underneath.
            // Both messages are needed and they are not interchangeable —
            // CheckDrivesStatusMessage recomputes the dashboard tiles and the chart,
            // while each row in "Your drives" only re-reads its own connectivity when it
            // hears NotifyDriveConnectivityMessage naming it. Sending the first alone
            // left the pie chart right and every row's status pill stale.
            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (Guid driveId in group.DriveIds)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(driveId));
            }

            if (result.IsFailure)
            {
                await Shell.Current.DisplayAlertAsync("Something went wrong!", result.Error.Description, "Ok");
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
