using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Time;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Helix.Infrastructure.Time;

internal sealed partial class CountdownService : ObservableObject, ICountdownService, IDisposable
{
    private readonly IDbContext _context;
    private readonly ILoggedInUser _loggedInUser;
    private readonly Timer _countdownTimer;

    public event EventHandler<int>? CountdownTick;
    public event EventHandler? CountdownFinished;

    public CountdownService(IDbContext context, ILoggedInUser loggedInUser)
    {
        _context = context;
        _loggedInUser = loggedInUser;

        _countdownTimer = new Timer(1000);
        _countdownTimer.Elapsed += OnCountdownTick;
    }

    [ObservableProperty]
    private int _secondsRemaining;

    public void Start(int initialSeconds)
    {
        SecondsRemaining = initialSeconds;

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
            // Optionally, log the exception or handle it accordingly.
            Console.WriteLine($"Error in Countdown Tick: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _countdownTimer.Elapsed -= OnCountdownTick;
        _countdownTimer.Dispose();
    }
}
