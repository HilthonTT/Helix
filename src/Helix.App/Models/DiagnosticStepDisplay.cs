using Helix.App.Icons;
using Helix.App.Resources.Languages;
using Helix.Application.Features.Drives.Contracts;

namespace Helix.App.Models;

internal sealed class DiagnosticStepDisplay
{
    public DiagnosticStepDisplay(DiagnosticResult result)
    {
        Title = TitleOf(result.Step);
        Message = MessageOf(result);
        Glyph = GlyphOf(result.Outcome);

        IsPassed = result.Outcome == DiagnosticOutcome.Passed;
        IsWarned = result.Outcome == DiagnosticOutcome.Warned;
        IsFailed = result.Outcome == DiagnosticOutcome.Failed;
        IsSkipped = result.Outcome == DiagnosticOutcome.Skipped;
    }

    public string Title { get; }

    public string Message { get; }

    public string Glyph { get; }

    public bool IsPassed { get; }

    public bool IsWarned { get; }

    public bool IsFailed { get; }

    public bool IsSkipped { get; }

    private static string TitleOf(DiagnosticStep step) => step switch
    {
        DiagnosticStep.HostResolution => AppResources.DiagnoseStepHostResolution,
        DiagnosticStep.HostReachable => AppResources.DiagnoseStepHostReachable,
        DiagnosticStep.ShareAndCredentials => AppResources.DiagnoseStepShareAndCredentials,
        DiagnosticStep.LetterAvailable => AppResources.DiagnoseStepLetterAvailable,
        _ => string.Empty,
    };

    private static string MessageOf(DiagnosticResult result) => result.Finding switch
    {
        DiagnosticFinding.HostSpellingResolved =>
            string.Format(AppResources.DiagnoseHostSpellingResolved, result.Detail),
        DiagnosticFinding.HostSpellingUnknown => AppResources.DiagnoseHostSpellingUnknown,
        DiagnosticFinding.PortOpen => string.Format(AppResources.DiagnosePortOpen, result.Detail),
        DiagnosticFinding.HostSilent => string.Format(AppResources.DiagnoseHostSilent, result.Detail),
        DiagnosticFinding.AlreadyMounted => AppResources.DiagnoseAlreadyMounted,
        DiagnosticFinding.CredentialsAccepted => AppResources.DiagnoseCredentialsAccepted,
        DiagnosticFinding.CredentialsUntested =>
            string.Format(AppResources.DiagnoseCredentialsUntested, result.Detail),
        DiagnosticFinding.CredentialsRejected => result.Detail ?? AppResources.UnexpectedError,
        DiagnosticFinding.SessionConflict => AppResources.DiagnoseSessionConflict,
        DiagnosticFinding.LetterFree => string.Format(AppResources.DiagnoseLetterFree, result.Detail),
        DiagnosticFinding.LetterHeldByThisShare =>
            string.Format(AppResources.DiagnoseLetterHeldByThisShare, result.Detail),
        DiagnosticFinding.LetterHeldByAnother =>
            string.Format(AppResources.DiagnoseLetterHeldByAnother, result.Detail),
        _ => AppResources.DiagnoseSkipped,
    };

    private static string GlyphOf(DiagnosticOutcome outcome) => outcome switch
    {
        DiagnosticOutcome.Passed => IconFont.CheckCircle,
        DiagnosticOutcome.Warned => IconFont.ExclamationTriangle,
        DiagnosticOutcome.Failed => IconFont.TimesCircle,
        _ => IconFont.MinusCircle,
    };
}
