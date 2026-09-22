using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
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

                _ = AnswerAsync(server, cancellationToken);

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

    private async Task AnswerAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        CommandRequest? request = null;

        try
        {
            await using (server)
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                string? payload = await reader.ReadLineAsync(cancellationToken);

                request = CommandRequest.Decode(payload ?? string.Empty);

                (bool ok, string message) = await ExecuteSafelyAsync(request);

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

        if (request?.Verb == CommandVerb.Quit)
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

            return (false, "Helix could not do that. The details are in its log.");
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
        _logger.LogInformation("Received the command-line request {Verb}.", request.Verb);

        switch (request.Verb)
        {
            case CommandVerb.None:
            case CommandVerb.Show:
                MainWindow.Restore();
                return (true, string.Empty);

            case CommandVerb.Help:
                return (true, CommandRequest.Usage);

            case CommandVerb.Unknown:
                return (false, $"'{request.Target}' is not something Helix understands.{CommandRequest.LineBreak}{CommandRequest.LineBreak}{CommandRequest.Usage}");

            case CommandVerb.Quit:
                return (true, "Helix is closing.");
        }

        if (!_loggedInUser.IsLoggedIn)
        {
            return (false, "Helix is not signed in. Sign in, then try again.");
        }

        if (App.ServiceProvider.GetRequiredService<IdleLockService>().IsLocked)
        {
            return (false, "Helix is locked. Unlock it, then try again.");
        }

        if (request.NeedsTarget && string.IsNullOrWhiteSpace(request.Target))
        {
            return (false, "Say which one, for example: --connect Z");
        }

        (bool Ok, string Message) outcome = request.Verb switch
        {
            CommandVerb.Status => await StatusAsync(),
            CommandVerb.ConnectAll => Report(
                await ScopedHandler.HandleAsync((ConnectAllDrives h) => h.Handle()),
                "Every drive is connected."),
            CommandVerb.DisconnectAll => Report(
                await ScopedHandler.HandleAsync((DisconnectAllDrives h) => h.Handle()),
                "Every drive is disconnected."),
            CommandVerb.Connect => await OnDriveAsync(request.Target, ConnectAsync),
            CommandVerb.Disconnect => await OnDriveAsync(request.Target, DisconnectAsync),
            CommandVerb.Wake => await OnDriveAsync(request.Target, WakeAsync),
            CommandVerb.ConnectGroup => await OnGroupAsync(request.Target, disconnect: false),
            CommandVerb.DisconnectGroup => await OnGroupAsync(request.Target, disconnect: true),
            _ => (false, CommandRequest.Usage),
        };

        if (request.Verb is not CommandVerb.Status and not CommandVerb.Wake)
        {
            await AfterChangeAsync();
        }

        return outcome;
    }

    private async Task<(bool, string)> StatusAsync()
    {
        Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (drives.IsFailure)
        {
            return (false, drives.Error.Description);
        }

        if (drives.Value.Count == 0)
        {
            return (true, "There are no drives.");
        }

        int width = drives.Value.Max(drive => drive.Name.Length);

        IEnumerable<string> lines = drives.Value
            .OrderBy(drive => drive.Letter, StringComparer.OrdinalIgnoreCase)
            .Select(drive => $"{drive.Letter}:  {drive.Name.PadRight(width)}  {(_nasConnector.IsLiveFrom(drive) ? "connected" : "disconnected")}");

        return (true, string.Join(CommandRequest.LineBreak, lines));
    }

    private static async Task<(bool, string)> OnDriveAsync(string target, Func<Drive, Task<(bool, string)>> action)
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

        return matches.Count switch
        {
            0 => (false, $"No drive is called '{target}'. Use a letter such as Z, or the drive's name."),
            1 => await action(matches[0]),
            _ => (false, $"More than one drive is called '{target}' ({string.Join(", ", matches.Select(d => d.Letter + ":"))}). Use its letter instead."),
        };
    }

    private static async Task<(bool, string)> ConnectAsync(Drive drive)
    {
        Result result = await ScopedHandler.HandleAsync((ConnectDrive h) => h.Handle(new ConnectDrive.Request(drive.Id)));

        Announce(drive.Id);

        return Report(result, $"{drive.Letter}: is connected.");
    }

    private static async Task<(bool, string)> DisconnectAsync(Drive drive)
    {
        Result result = await ScopedHandler.HandleAsync((DisconnectDrive h) => h.Handle(new DisconnectDrive.Request(drive.Id)));

        Announce(drive.Id);

        return Report(result, $"{drive.Letter}: is disconnected.");
    }

    private static async Task<(bool, string)> WakeAsync(Drive drive)
    {
        Result result = await ScopedHandler.HandleAsync((WakeDrive h) => h.Handle(new WakeDrive.Request(drive.Id)));

        return Report(result, $"A wake-up packet was sent for {drive.Letter}:.");
    }

    private static async Task<(bool, string)> OnGroupAsync(string target, bool disconnect)
    {
        Result<List<DriveGroup>> groups = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
        if (groups.IsFailure)
        {
            return (false, groups.Error.Description);
        }

        DriveGroup? group = groups.Value.FirstOrDefault(g => string.Equals(g.Name, target.Trim(), StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return (false, $"No drive group is called '{target}'.");
        }

        Result result = await ScopedHandler.HandleAsync(
            (ConnectDriveGroup h) => h.Handle(new ConnectDriveGroup.Request(group.Id, disconnect)));

        return Report(result, disconnect ? $"'{group.Name}' is disconnected." : $"'{group.Name}' is connected.");
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
