namespace Helix.Infrastructure.Connector;

internal sealed class HostGate(int stallMilliseconds)
{
    private const int MinimumWaitMilliseconds = 1_000;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _turnedOverAt = Environment.TickCount64;

    public async Task<bool> EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            long turnedOverAt = Interlocked.Read(ref _turnedOverAt);
            long heldFor = Environment.TickCount64 - turnedOverAt;
            int wait = (int)Math.Max(MinimumWaitMilliseconds, stallMilliseconds - heldFor);

            if (await _semaphore.WaitAsync(wait, cancellationToken))
            {
                Interlocked.Exchange(ref _turnedOverAt, Environment.TickCount64);
                return true;
            }

            if (Interlocked.Read(ref _turnedOverAt) == turnedOverAt &&
                Environment.TickCount64 - turnedOverAt >= stallMilliseconds)
            {
                return false;
            }
        }
    }

    public void Exit()
    {
        Interlocked.Exchange(ref _turnedOverAt, Environment.TickCount64);
        _semaphore.Release();
    }
}
