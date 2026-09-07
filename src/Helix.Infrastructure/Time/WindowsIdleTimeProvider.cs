using Helix.Application.Abstractions.Time;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Time;

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
