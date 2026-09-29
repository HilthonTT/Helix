using Helix.App.Resources.Languages;

namespace Helix.App.Common;

internal static class DisconnectConfirmation
{
    public static async Task<bool> ConfirmAsync(int connectedCount)
    {
        if (connectedCount == 0)
        {
            return true;
        }

        string message = connectedCount == 1
            ? AppResources.DisconnectConfirmOne
            : string.Format(AppResources.DisconnectConfirmMany, connectedCount);

        return await Shell.Current.DisplayAlertAsync(
            AppResources.DisconnectConfirmTitle,
            message,
            AppResources.Disconnect,
            AppResources.Cancel);
    }
}
