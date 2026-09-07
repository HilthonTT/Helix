namespace Helix.Application.Abstractions.Connector;

public sealed record WatchedDrive(Guid Id, string Letter, bool AutoConnect = true);

public sealed record DriveConnectivityChange(
    Guid DriveId,
    string Letter,
    bool IsConnected,
    bool AutoConnect = true);

public interface IDriveMonitor
{
    event EventHandler<IReadOnlyList<DriveConnectivityChange>>? ConnectivityChanged;

    bool IsRunning { get; }

    void Watch(IReadOnlyCollection<WatchedDrive> drives);

    IDisposable Suppress(IEnumerable<string> letters);

    void Start(TimeSpan interval);

    void Stop();

    Task PollAsync(CancellationToken cancellationToken = default);
}
