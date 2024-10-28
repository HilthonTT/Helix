namespace Helix.Application.Abstractions.Time;

public interface ICountdownService
{
    void Start(int initialSeconds);

    void Stop();

    void Reset(int newInitialSeconds = 0);

    void Resume();

    event EventHandler<int>? CountdownTick;

    event EventHandler? CountdownFinished;
}
