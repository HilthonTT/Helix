using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Helix.Infrastructure.Time;

internal sealed partial class CountdownService : ObservableObject, ICountdownService, IDisposable
{
    private readonly Timer _countdownTimer;
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

    public void Start(int initialSeconds)
    {
        // A non-positive countdown would tick straight to "finished" a second later and
        // minimize the window; there is nothing to run.
        if (initialSeconds <= 0)
        {
            Reset();
            return;
        }

        SecondsRemaining = initialSeconds;

        // Stop first: restarting an already-running timer leaves its current interval in
        // flight, so the first tick could land almost immediately after a re-arm.
        _countdownTimer.Stop();
        _countdownTimer.Start();
    }

    public void Stop()
    {
        _countdownTimer.Stop();
    }

    public void Reset(int newInitialSeconds = 0)
    {
        Stop();
        SecondsRemaining = newInitialSeconds;
    }

    public void Resume()
    {
        if (SecondsRemaining > 0 && !_countdownTimer.Enabled)
        {
            _countdownTimer.Start();
        }
    }

    private void OnCountdownTick(object? sender, ElapsedEventArgs e)
    {
        try
        {
            if (SecondsRemaining > 0)
            {
                SecondsRemaining--;
                CountdownTick?.Invoke(this, SecondsRemaining);
            }

            if (SecondsRemaining <= 0)
            {
                Stop();
                CountdownFinished?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Stop();

            // A subscriber threw on the timer thread. Stopped rather than left ticking,
            // and written to the log file rather than the console, which no released
            // build has.
            _logger.LogError(ex, "The auto-minimize countdown stopped because a tick handler threw.");
        }
    }

    public void Dispose()
    {
        _countdownTimer.Elapsed -= OnCountdownTick;
        _countdownTimer.Dispose();
    }
}
