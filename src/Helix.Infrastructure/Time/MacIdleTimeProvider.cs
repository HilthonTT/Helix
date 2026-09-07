#if MACCATALYST
using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Time;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacIdleTimeProvider(ILogger<MacIdleTimeProvider> logger) : IIdleTimeProvider
{
    private const int HidSystemState = 1;

    private const uint AnyInputEvent = uint.MaxValue;

    public bool IsSupported => true;

    public TimeSpan GetIdleTime()
    {
        try
        {
            double seconds = CGEventSourceSecondsSinceLastEventType(HidSystemState, AnyInputEvent);

            return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            logger.LogDebug(ex, "CoreGraphics did not answer for the idle time; treating the machine as in use.");

            return TimeSpan.Zero;
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern double CGEventSourceSecondsSinceLastEventType(int sourceStateId, uint eventType);
}
#endif
