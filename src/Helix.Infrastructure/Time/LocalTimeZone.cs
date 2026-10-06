using Helix.Application.Abstractions.Time;

namespace Helix.Infrastructure.Time;

internal sealed class LocalTimeZone : ILocalTimeZone
{
    public TimeZoneInfo Current
    {
        get
        {
            TimeZoneInfo.ClearCachedData();

            return TimeZoneInfo.Local;
        }
    }
}
