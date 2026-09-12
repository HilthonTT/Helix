using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Services;
using Helix.Application.Features.DriveGroups.Commands;
using Microsoft.Extensions.Logging;

namespace Helix.App.Common;

internal static class DriveGroupActions
{
    public static async Task ToggleAsync(DriveGroupDisplay group, bool disconnect)
    {
        if (group.IsBusy)
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
            AppLog.For(typeof(DriveGroupActions)).LogError(ex, "A drive group action failed.");
        }
        finally
        {
            group.IsBusy = false;
        }
    }
}
