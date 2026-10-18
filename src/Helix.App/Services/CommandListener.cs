using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.DriveGroups.Commands;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Helix.App.Services;

internal sealed class CommandListener
{
    public const string Success = "+";
    public const string Failure = "-";

    private readonly ILoggedInUser _loggedInUser;
    private readonly INasConnector _nasConnector;
    private readonly IDriveMonitor _monitor;
    private readonly ILogger<CommandListener> _logger;

    private CancellationTokenSource? _cancellation;

    public CommandListener(
        ILoggedInUser loggedInUser,
        INasConnector nasConnector,
        IDriveMonitor monitor,
        ILogger<CommandListener> logger)
    {
        _loggedInUser = loggedInUser;
        _nasConnector = nasConnector;
        _monitor = monitor;
        _logger = logger;
    }

    public static string PipeName => $"Helix.App.Command.{Process.GetCurrentProcess().SessionId}";

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        CancellationToken token = _cancellation.Token;

        _ = Task.Run(() => ListenAsync(token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken);

                NamedPipeServerStream connection = server;

                _ = Task.Run(() => AnswerAsync(connection, cancellationToken), CancellationToken.None);

                server = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The command-line listener faulted; it will keep listening.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            finally
            {
                if (server is not null)
                {
                    await server.DisposeAsync();
                }
            }
        }
    }

    public async Task RunLinkAsync(CommandRequest request)
    {
        (bool ok, string message) = await ExecuteSafelyAsync(request);

        TellLinkOutcome(ok, message);

        if (request.Verb == CommandVerb.Quit && ok)
        {
            Quit();
        }
    }

    private void TellLinkOutcome(bool ok, string message)
    {
        if (message.Length == 0)
        {
            return;
        }

        TrayIconService tray = App.ServiceProvider.GetRequiredService<TrayIconService>();

        if (tray.IsRunning)
        {
            tray.Notify(AppInfo.Current.Name, message);
            return;
        }

        if (ok)
        {
            Notifier.Success(message);
        }
        else
        {
            Notifier.Error(message);
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        CommandRequest? request = null;
        bool succeeded = false;

        try
        {
            await using (server)
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                string? payload = await reader.ReadLineAsync(cancellationToken);

                request = CommandRequest.Decode(payload ?? string.Empty);

                (bool ok, string message) = await ExecuteSafelyAsync(request);

                succeeded = ok;

                if (request.FromLink)
                {
                    TellLinkOutcome(ok, message);
                }

                await writer.WriteLineAsync((ok ? Success : Failure) + message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "A command-line client went away before it was answered.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A command-line request could not be answered.");
        }

        if (request?.Verb == CommandVerb.Quit && succeeded)
        {
            Quit();
        }
    }

    private async Task<(bool Ok, string Message)> ExecuteSafelyAsync(CommandRequest request)
    {
        try
        {
            return await ExecuteAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The command-line request {Verb} failed.", request.Verb);

            return (false, Say(request.FromLink, "Helix could not do that. The details are in its log.", AppResources.LinkFailed));
        }
    }

    private void Quit()
    {
        try
        {
            App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not take the tray icon down before quitting.");
        }

        MainWindow.Exit();
    }

    private async Task<(bool Ok, string Message)> ExecuteAsync(CommandRequest request)
    {
        _logger.LogInformation(
            "Received the {Source} request {Verb}.",
            request.FromLink ? "link" : "command-line",
            request.Verb);

        bool link = request.FromLink;

        switch (request.Verb)
        {
            case CommandVerb.None:
            case CommandVerb.Show:
                MainWindow.Restore();
                return (true, string.Empty);

            case CommandVerb.Help:
                return (true, link ? string.Empty : CommandRequest.Usage);

            case CommandVerb.Unknown:
                return (false, Say(
                    link,
                    $"'{request.Target}' is not something Helix understands.{CommandRequest.LineBreak}{CommandRequest.LineBreak}{CommandRequest.Usage}",
                    string.Format(AppResources.LinkUnknown, request.Target)));

            case CommandVerb.Quit:
                // A link asks before it quits, and that question must not go up over the lock
                // for whoever is at the keyboard to answer — the tray refuses Exit there too.
                if (request.NeedsConfirmation && IdleLockService.ScreenLocked)
                {
                    return (false, Say(link, "Helix is locked. Unlock it, then try again.", AppResources.LinkLocked));
                }

                if (request.NeedsConfirmation && !await ConfirmLinkAsync(request))
                {
                    return (false, string.Empty);
                }

                return (true, Say(link, "Helix is closing.", string.Empty));
        }

        if (!_loggedInUser.IsLoggedIn)
        {
            return (false, Say(link, "Helix is not signed in. Sign in, then try again.", AppResources.LinkNotSignedIn));
        }

        if (App.ServiceProvider.GetRequiredService<IdleLockService>().IsLocked)
        {
            return (false, Say(link, "Helix is locked. Unlock it, then try again.", AppResources.LinkLocked));
        }

        if (request.NeedsTarget && string.IsNullOrWhiteSpace(request.Target))
        {
            return (false, Say(link, "Say which one, for example: --connect Z", AppResources.LinkNeedsTarget));
        }

        if (request.NeedsConfirmation && !await ConfirmLinkAsync(request))
        {
            return (false, string.Empty);
        }

        (bool Ok, string Message) outcome = request.Verb switch
        {
            CommandVerb.Status => await StatusAsync(link),
            CommandVerb.ConnectAll => Report(
                await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle()),
                Say(link, "Every drive is connected.", AppResources.HotkeyConnectedAll)),
            CommandVerb.DisconnectAll => Report(
                await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle()),
                Say(link, "Every drive is disconnected.", AppResources.HotkeyDisconnectedAll)),
            CommandVerb.Connect => await OnDriveAsync(request.Target, link, ConnectAsync),
            CommandVerb.Disconnect => await OnDriveAsync(request.Target, link, DisconnectAsync),
            CommandVerb.Wake => await OnDriveAsync(request.Target, link, WakeAsync),
            CommandVerb.ConnectGroup => await OnGroupAsync(request.Target, link, disconnect: false),
            CommandVerb.DisconnectGroup => await OnGroupAsync(request.Target, link, disconnect: true),
            _ => (false, CommandRequest.Usage),
        };

        if (request.Verb is not CommandVerb.Status and not CommandVerb.Wake)
        {
            await AfterChangeAsync();
        }

        return outcome;
    }

    private static string Say(bool link, string english, string localized) => link ? localized : english;

    private static async Task<bool> ConfirmLinkAsync(CommandRequest request) =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is null)
            {
                return false;
            }

            MainWindow.Restore();

            string question = request.Verb switch
            {
                CommandVerb.DisconnectAll => AppResources.LinkConfirmDisconnectAll,
                CommandVerb.Disconnect => string.Format(AppResources.LinkConfirmDisconnect, request.Target),
                CommandVerb.DisconnectGroup => string.Format(AppResources.LinkConfirmDisconnectGroup, request.Target),
                _ => AppResources.LinkConfirmQuit,
            };

            bool confirmed = await Shell.Current.DisplayAlertAsync(
                AppResources.LinkConfirmTitle,
                question,
                request.Verb == CommandVerb.Quit ? AppResources.TrayExit : AppResources.Disconnect,
                AppResources.Cancel);

            return confirmed && !IdleLockService.ScreenLocked;
        });

    private async Task<(bool, string)> StatusAsync(bool link)
    {
        Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (drives.IsFailure)
        {
            return (false, drives.Error.Description);
        }

        if (drives.Value.Count == 0)
        {
            return (true, Say(link, "There are no drives.", AppResources.LinkNoDrives));
        }

        if (link)
        {
            int up = drives.Value.Count(_nasConnector.IsLiveFrom);

            return (true, string.Format(AppResources.LinkStatus, up, drives.Value.Count));
        }

        int width = drives.Value.Max(drive => drive.Name.Length);

        IEnumerable<string> lines = drives.Value
            .OrderBy(drive => drive.Letter, StringComparer.OrdinalIgnoreCase)
            .Select(drive => $"{drive.Letter}:  {drive.Name.PadRight(width)}  {(_nasConnector.IsLiveFrom(drive) ? "connected" : "disconnected")}");

        return (true, string.Join(CommandRequest.LineBreak, lines));
    }

    private static async Task<(bool, string)> OnDriveAsync(
        string target,
        bool link,
        Func<Drive, bool, Task<(bool, string)>> action)
    {
        Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (drives.IsFailure)
        {
            return (false, drives.Error.Description);
        }

        string wanted = target.Trim().TrimEnd(':', '\\');

        List<Drive> matches = wanted.Length == 1
            ? [.. drives.Value.Where(drive => string.Equals(drive.Letter, wanted, StringComparison.OrdinalIgnoreCase))]
            : [];

        if (matches.Count == 0)
        {
            matches = [.. drives.Value.Where(drive => string.Equals(drive.Name, wanted, StringComparison.OrdinalIgnoreCase))];
        }

        string letters = string.Join(", ", matches.Select(d => d.Letter + ":"));

        return matches.Count switch
        {
            0 => (false, Say(
                link,
                $"No drive is called '{target}'. Use a letter such as Z, or the drive's name.",
                string.Format(AppResources.LinkNoDrive, target))),
            1 => await action(matches[0], link),
            _ => (false, Say(
                link,
                $"More than one drive is called '{target}' ({letters}). Use its letter instead.",
                string.Format(AppResources.LinkAmbiguousDrive, target, letters))),
        };
    }

    private static async Task<(bool, string)> ConnectAsync(Drive drive, bool link)
    {
        Result result = await ScopedHandler.HandleAsync((ConnectDrive h) => h.Handle(new ConnectDrive.Request(drive.Id)));

        Announce(drive.Id);

        return Report(result, Say(link, $"{drive.Letter}: is connected.", string.Format(AppResources.LinkConnected, $"{drive.Letter}:")));
    }

    private static async Task<(bool, string)> DisconnectAsync(Drive drive, bool link)
    {
        Result result = await ScopedHandler.HandleAsync((DisconnectDrive h) => h.Handle(new DisconnectDrive.Request(drive.Id)));

        Announce(drive.Id);

        return Report(result, Say(link, $"{drive.Letter}: is disconnected.", string.Format(AppResources.LinkDisconnected, $"{drive.Letter}:")));
    }

    private static async Task<(bool, string)> WakeAsync(Drive drive, bool link)
    {
        Result result = await ScopedHandler.HandleAsync((WakeDrive h) => h.Handle(new WakeDrive.Request(drive.Id)));

        return Report(result, Say(link, $"A wake-up packet was sent for {drive.Letter}:.", string.Format(AppResources.WakeSent, drive.Name)));
    }

    private static async Task<(bool, string)> OnGroupAsync(string target, bool link, bool disconnect)
    {
        Result<List<DriveGroup>> groups = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
        if (groups.IsFailure)
        {
            return (false, groups.Error.Description);
        }

        DriveGroup? group = groups.Value.FirstOrDefault(g => string.Equals(g.Name, target.Trim(), StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return (false, Say(link, $"No drive group is called '{target}'.", string.Format(AppResources.LinkNoGroup, target)));
        }

        Result result = await ScopedHandler.HandleAsync(
            (ConnectDriveGroup h) => h.Handle(new ConnectDriveGroup.Request(group.Id, disconnect)));

        string success = disconnect
            ? Say(link, $"'{group.Name}' is disconnected.", string.Format(AppResources.LinkDisconnected, group.Name))
            : Say(link, $"'{group.Name}' is connected.", string.Format(AppResources.LinkConnected, group.Name));

        return Report(result, success);
    }

    private static (bool, string) Report(Result result, string success) =>
        result.IsSuccess ? (true, success) : (false, result.Error.Description);

    private static void Announce(Guid driveId) =>
        MainThread.BeginInvokeOnMainThread(() =>
            WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(driveId)));

    private async Task AfterChangeAsync()
    {
        try
        {
            await _monitor.PollAsync();

            MainThread.BeginInvokeOnMainThread(() =>
                WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage()));

            await App.ServiceProvider.GetRequiredService<TrayIconService>().RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not refresh the drive list after a command-line request.");
        }
    }
}
