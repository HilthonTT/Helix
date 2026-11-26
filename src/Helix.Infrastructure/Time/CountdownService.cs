using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Helix.Infrastructure.Time;

internal sealed partial class CountdownService : ObservableObject, ICountdownService, IDisposable
{
    private readonly Timer _countdownTimer;
    private readonly Lock _gate = new();
    private readonly ILogger<CountdownService> _logger;

    public event EventHandler<int>? CountdownTick;
    public event EventHandler? CountdownFinished;

    public CountdownService(ILogger<CountdownService> logger)
    {
        _logger = logger;

        _countdownTimer = new Timer(1000);
        _countdownTimer.Elapsed += OnCountdownTick;
    }

    [ObservableProperty]
    public partial int SecondsRemaining { get; set; }

    public bool IsRunning => _countdownTimer.Enabled;

    public void Start(int initialSeconds)
    {
        if (initialSeconds <= 0)
        {
            Reset();
            return;
        }

        lock (_gate)
        {
            SecondsRemaining = initialSeconds;

            _countdownTimer.Stop();
            _countdownTimer.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _countdownTimer.Stop();
        }
    }

    public void Reset(int newInitialSeconds = 0)
    {
        lock (_gate)
        {
            _countdownTimer.Stop();
            SecondsRemaining = newInitialSeconds;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (SecondsRemaining > 0 && !_countdownTimer.Enabled)
            {
                _countdownTimer.Start();
            }
        }
    }

    internal void OnCountdownTick(object? sender, ElapsedEventArgs? e)
    {
        int? remaining = null;
        bool finished;

        lock (_gate)
        {
            if (!_countdownTimer.Enabled)
            {
                return;
            }

            if (SecondsRemaining > 0)
            {
                SecondsRemaining--;
                remaining = SecondsRemaining;
            }

            finished = SecondsRemaining <= 0;
            if (finished)
            {
                _countdownTimer.Stop();
            }
        }

        try
        {
            if (remaining is int seconds)
            {
                CountdownTick?.Invoke(this, seconds);
            }

            if (finished)
            {
                CountdownFinished?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Stop();

            _logger.LogError(ex, "The auto-minimize countdown stopped because a tick handler threw.");
        }
    }

    public void Dispose()
    {
        _countdownTimer.Elapsed -= OnCountdownTick;
        _countdownTimer.Dispose();
    }
}
