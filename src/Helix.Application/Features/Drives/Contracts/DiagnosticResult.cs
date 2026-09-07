namespace Helix.Application.Features.Drives.Contracts;

public sealed record DiagnosticResult(
    DiagnosticStep Step,
    DiagnosticOutcome Outcome,
    DiagnosticFinding Finding,
    string? Detail = null);
