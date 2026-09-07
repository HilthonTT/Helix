namespace Helix.Application.Features.Drives.Contracts;

public enum DiagnosticStep
{
    HostResolution = 0,
    HostReachable = 1,
    ShareAndCredentials = 2,
    LetterAvailable = 3,
}
