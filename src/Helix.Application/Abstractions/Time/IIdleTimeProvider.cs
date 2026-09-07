namespace Helix.Application.Abstractions.Time;

public interface IIdleTimeProvider
{
    bool IsSupported { get; }

    TimeSpan GetIdleTime();
}
