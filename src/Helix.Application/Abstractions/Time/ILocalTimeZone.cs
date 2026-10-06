namespace Helix.Application.Abstractions.Time;

public interface ILocalTimeZone
{
    TimeZoneInfo Current { get; }
}
