using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<DriveMonitor> _logger;
    private readonly Lock _gate = new();

    /// <summary>Watched drives, keyed by uppercase letter.</summary>
    private Dictionary<string, WatchedDrive> _watched = [];

    /// <summary>Last observed connectivity per watched letter — the diff baseline.</summary>
    private Dictionary<string, bool> _baseline = [];

    /// <summary>
    /// Letters whose state Helix is changing on purpose, and how many callers are
    /// currently saying so. Counted rather than a flag: two overlapping operations may
    /// name the same letter, and the first to finish must not uncover it for the second.
    /// </summary>
    private readonly Dictionary<string, int> _suppressed = [];

    /// <summary>
    /// When each letter's suppression was last released, on <see cref="_clock"/>. A poll
    /// reads the mounted set outside the gate, so a release can land between that read
    /// and the compare: the release re-seeds the baseline from the finished operation,
    /// and the stale snapshot then differs from it by exactly the change the suppression
    /// existed to swallow. A poll skips a letter released after its snapshot began, and
    /// only that poll — the next one reads a set that already reflects the release.
    /// </summary>
    private readonly Dictionary<string, long> _releasedAt = [];

    private long _clock;

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public DriveMonitor(INasConnector nasConnector, ILogger<DriveMonitor> logger)
    {
        _nasConnector = nasConnector;
        _logger = logger;
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

    public IDisposable Suppress(IEnumerable<string> letters)
    {
        string[] normalized = [.. letters
            .Where(letter => !string.IsNullOrWhiteSpace(letter))
            .Select(Normalize)
            .Distinct()];

        if (normalized.Length == 0)
        {
            return NullSuppression.Instance;
        }

        lock (_gate)
        {
            foreach (string letter in normalized)
            {
                _suppressed[letter] = _suppressed.TryGetValue(letter, out int count) ? count + 1 : 1;
            }
        }

        return new Suppression(this, normalized);
    }

    /// <summary>
    /// Ends one suppression and re-seeds the baseline of the letters it covered from
    /// what is actually mounted now.
    /// </summary>
    /// <remarks>
    /// Re-seeding rather than simply resuming: the caller has just mounted or unmounted
    /// these letters, so their new state is the state everything downstream should
    /// consider normal. Resuming with the old baseline would hand the next poll exactly
    /// the change the suppression existed to swallow.
    /// </remarks>
    private void Release(string[] letters)
    {
        // Read outside the lock — it is a bitmask query, but it is still not this
        // object's business to hold its own gate across a platform call.
        HashSet<string> connected = _nasConnector.GetConnectedLetters();

        lock (_gate)
        {
            foreach (string letter in letters)
            {
                if (!_suppressed.TryGetValue(letter, out int count))
                {
                    continue;
                }

                if (count > 1)
                {
                    // Somebody else is still working on this letter; leave the baseline
                    // to whoever releases last.
                    _suppressed[letter] = count - 1;
                    continue;
                }

                _suppressed.Remove(letter);
                _releasedAt[letter] = ++_clock;

                if (_baseline.ContainsKey(letter))
                {
                    _baseline[letter] = connected.Contains(letter);
                }
            }
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
            _logger.LogError(ex, "The drive monitor loop faulted and has stopped polling.");
        }
    }

    private void Poll()
    {
        List<DriveConnectivityChange> changes = [];

        // Read and compared under one lock. The timer loop and PollAsync run this
        // concurrently, and a set read outside the lock by one could be compared inside
        // it after the other had already moved the baseline - a stale snapshot reported
        // as a fresh drop, a toast and an audit row for a drive that had just come up.
        // The read is a logical-drive bitmask; holding the lock across it costs nothing.
        lock (_gate)
        {
            long snapshotStartedAt = _clock;

            HashSet<string> connected = _nasConnector.GetConnectedLetters();

            foreach ((string letter, WatchedDrive drive) in _watched)
            {
                if (_suppressed.ContainsKey(letter) || WasReleasedAfter(letter, snapshotStartedAt))
                {
                    // Helix is mid-mount or mid-unmount on this one. The baseline is
                    // left untouched as well as unreported: whatever it reads right now
                    // is a half-finished operation, and the release re-seeds it from the
                    // finished one.
                    continue;
                }

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

    private bool WasReleasedAfter(string letter, long clock) =>
        _releasedAt.TryGetValue(letter, out long releasedAt) && releasedAt > clock;

    private static string Normalize(string letter) => letter.Trim().ToUpperInvariant();

    /// <summary>The handle handed back by <see cref="Suppress"/>.</summary>
    private sealed class Suppression(DriveMonitor monitor, string[] letters) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            // Idempotent: a `using` inside a method that also disposes by hand, or a
            // double dispose from a retry, must not decrement the count twice and
            // uncover a letter another caller is still holding.
            if (_released)
            {
                return;
            }

            _released = true;

            monitor.Release(letters);
        }
    }

    /// <summary>Returned when there is nothing to suppress, so callers can still `using`.</summary>
    private sealed class NullSuppression : IDisposable
    {
        public static readonly NullSuppression Instance = new();

        public void Dispose()
        {
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
