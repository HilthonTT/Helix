#if MACCATALYST
using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Time;

/// <summary>
/// Reads the system-wide idle time from CoreGraphics.
/// </summary>
/// <remarks>
/// <c>CGEventSourceSecondsSinceLastEventType</c> against the HID system state is the
/// macOS counterpart to <c>GetLastInputInfo</c>: it answers for the whole machine rather
/// than for this process, and it needs no Accessibility permission to do it — which the
/// event-tap route would, and which would put a system prompt in front of a user who only
/// turned on a lock timer.
/// </remarks>
[SupportedOSPlatform("maccatalyst")]
internal sealed class MacIdleTimeProvider(ILogger<MacIdleTimeProvider> logger) : IIdleTimeProvider
{
    /// <summary>kCGEventSourceStateHIDSystemState — the hardware, not this app's queue.</summary>
    private const int HidSystemState = 1;

    /// <summary>kCGAnyInputEventType.</summary>
    private const uint AnyInputEvent = uint.MaxValue;

    public bool IsSupported => true;

    public TimeSpan GetIdleTime()
    {
        try
        {
            double seconds = CGEventSourceSecondsSinceLastEventType(HidSystemState, AnyInputEvent);

            // A negative or nonsensical answer is not an idle machine.
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
