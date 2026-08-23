using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Time;

/// <summary>
/// Reads the system-wide idle time from Win32.
/// </summary>
/// <remarks>
/// <c>GetLastInputInfo</c> is the only way to ask this without installing an input hook,
/// and it is the same thing the screensaver and the lock screen go on. It reports across
/// every process on the desktop, which is what makes it the right question: the user is
/// idle or they are not, whatever window they are idle in front of.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsIdleTimeProvider(ILogger<WindowsIdleTimeProvider> logger) : IIdleTimeProvider
{
    public bool IsSupported => true;

    public TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>(),
        };

        if (!GetLastInputInfo(ref info))
        {
            logger.LogDebug("The system did not report its last input time; treating the machine as in use.");

            return TimeSpan.Zero;
        }

        // Both are 32-bit tick counts that wrap roughly every 49 days. The unchecked
        // subtraction is what makes the wrap harmless: the difference stays right even
        // when the later value is numerically the smaller of the two.
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);

        return TimeSpan.FromMilliseconds(elapsed);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
