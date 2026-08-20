using Helix.Application.Abstractions.Connector;
using System.Diagnostics;

namespace Helix.Infrastructure.Connector;

/// <summary>
/// Polls <see cref="INasConnector.GetConnectedLetters"/> on an interval and reports
/// which watched drives changed state.
/// </summary>
/// <remarks>
/// Only the cheap half of the connectivity question is asked here. Enumerating logical
/// drive letters is a bitmask query, whereas reading a volume's size or free space
/// performs I/O against the share and can hang for as long as the network takes to
/// give up. Capacity is therefore left to the caller to fetch off the UI thread.
/// </remarks>
internal sealed class DriveMonitor : IDriveMonitor, IDisposable
{
    private readonly INasConnector _nasConnector;
    private readonly Lock _gate = new();

    /// <summary>Watched drives, keyed by uppercase letter.</summary>
    private Dictionary<string, WatchedDrive> _watched = [];

    /// <summary>Last observed connectivity per watched letter — the diff baseline.</summary>
    private Dictionary<string, bool> _baseline = [];

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public DriveMonitor(INasConnector nasConnector)
    {
        _nasConnector = nasConnector;
    }

    public event EventHandler<IReadOnlyList<DriveConnectivityChange>>? ConnectivityChanged;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Watch(IReadOnlyCollection<WatchedDrive> drives)
    {
        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        lock (_gate)
        {
            _watched = drives
                .Where(drive => !string.IsNullOrWhiteSpace(drive.Letter))
                .GroupBy(drive => Normalize(drive.Letter))
                .ToDictionary(group => group.Key, group => group.First());

            // Seed from reality rather than from the previous baseline: a drive that is
            // already offline when it starts being watched has not just dropped.
            _baseline = _watched.Keys.ToDictionary(letter => letter, connected.Contains);
        }
    }

    public void Start(TimeSpan interval)
    {
        if (IsRunning)
        {
            return;
        }

        Stop();

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(interval, _cancellation.Token);
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down by a concurrent Stop/Dispose — nothing to do.
        }
        finally
        {
            cancellation.Dispose();
        }

        _loop = null;
    }

    public Task PollAsync(CancellationToken cancellationToken = default)
    {
        // Task.Run so a caller on the UI thread is never blocked by the sweep.
        return Task.Run(Poll, cancellationToken);
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            // The loop is fire-and-forget; never let a fault reach the finalizer thread.
            Debug.WriteLine($"Helix: drive monitor loop faulted: {ex}");
        }
    }

    private void Poll()
    {
        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        List<DriveConnectivityChange> changes = [];

        lock (_gate)
        {
            foreach ((string letter, WatchedDrive drive) in _watched)
            {
                bool isConnected = connected.Contains(letter);

                if (_baseline.TryGetValue(letter, out bool was) && was == isConnected)
                {
                    continue;
                }

                _baseline[letter] = isConnected;
                changes.Add(new DriveConnectivityChange(drive.Id, drive.Letter, isConnected, drive.AutoConnect));
            }
        }

        if (changes.Count > 0)
        {
            ConnectivityChanged?.Invoke(this, changes);
        }
    }

    private static string Normalize(string letter) => letter.Trim().ToUpperInvariant();

    public void Dispose()
    {
        Stop();
    }
}
