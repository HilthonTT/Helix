namespace Helix.Application.Features.Drives.Contracts;

public sealed record DriveDiagnosis(
    Guid DriveId,
    string Letter,
    string Name,
    string Host,
    IReadOnlyList<DiagnosticResult> Steps)
{
    public bool HasFailure => Steps.Any(step => step.Outcome == DiagnosticOutcome.Failed);

    public DiagnosticResult? FirstFailure =>
        Steps.FirstOrDefault(step => step.Outcome == DiagnosticOutcome.Failed);
}
