using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.Application.Abstractions.Connector;

namespace Helix.App.Common;

internal static class DisconnectConfirmation
{
    public static async Task<bool> ConfirmAsync(IReadOnlyCollection<string> mountedLetters, bool onlyWhenInUse = false)
    {
        if (mountedLetters.Count == 0)
        {
            return true;
        }

        IReadOnlyList<string> inUse = await InUseAsync(mountedLetters);

        if (inUse.Count == 0 && onlyWhenInUse)
        {
            return true;
        }

        string message = mountedLetters.Count == 1
            ? AppResources.DisconnectConfirmOne
            : string.Format(AppResources.DisconnectConfirmMany, mountedLetters.Count);

        if (inUse.Count != 0)
        {
            message += Environment.NewLine + Environment.NewLine +
                string.Format(AppResources.DisconnectInUse, string.Join(", ", inUse));
        }

        bool confirmed = await Shell.Current.DisplayAlertAsync(
            AppResources.DisconnectConfirmTitle,
            message,
            AppResources.Disconnect,
            AppResources.Cancel);

        return confirmed && !IdleLockService.ScreenLocked;
    }

    private static Task<IReadOnlyList<string>> InUseAsync(IReadOnlyCollection<string> letters)
    {
        INasConnector nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();

        return Task.Run<IReadOnlyList<string>>(() =>
        [
            .. letters
                .Where(letter => nasConnector.CountOpenHandles(letter) > 0)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(letter => $"{letter}:"),
        ]);
    }
}
