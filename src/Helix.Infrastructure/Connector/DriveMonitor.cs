using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Connector;

internal sealed class DriveMonitor : IDriveMonitor, IDisposable
{
    private readonly INasConnector _nasConnector;
    private readonly ILogger<DriveMonitor> _logger;
    private readonly Lock _gate = new();

    private Dictionary<string, WatchedDrive> _watched = [];

    private Dictionary<string, bool> _baseline = [];

    private readonly Dictionary<string, int> _suppressed = [];

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

            Dictionary<string, bool> previous = _baseline;

            _baseline = _watched.Keys.ToDictionary(
                letter => letter,
                letter => previous.TryGetValue(letter, out bool known) ? known : connected.Contains(letter));
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

    private void Release(string[] letters)
    {
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
        }
        finally
        {
            cancellation.Dispose();
        }

        _loop = null;
    }

    public Task PollAsync(CancellationToken cancellationToken = default)
    {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The drive monitor loop faulted and has stopped polling.");
        }
    }

    private void Poll()
    {
        List<DriveConnectivityChange> changes = [];

        lock (_gate)
        {
            long snapshotStartedAt = _clock;

            HashSet<string> connected = _nasConnector.GetConnectedLetters();

            foreach ((string letter, WatchedDrive drive) in _watched)
            {
                if (_suppressed.ContainsKey(letter) || WasReleasedAfter(letter, snapshotStartedAt))
                {
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

    private sealed class Suppression(DriveMonitor monitor, string[] letters) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            monitor.Release(letters);
        }
    }

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
