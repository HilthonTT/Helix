namespace Helix.Application.Features.Drives.Contracts;

public enum DiagnosticFinding
{
    None = 0,
    HostSpellingResolved = 1,
    HostSpellingUnknown = 2,
    PortOpen = 3,
    HostSilent = 4,
    AlreadyMounted = 5,
    CredentialsAccepted = 6,
    CredentialsUntested = 7,
    CredentialsRejected = 8,
    SessionConflict = 9,
    LetterFree = 10,
    LetterHeldByThisShare = 11,
    LetterHeldByAnother = 12,
}
