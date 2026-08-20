namespace Helix.Application.Core.Errors;

internal static class ValidationErrors
{
    public static readonly Error MissingFields = Error.Problem(
        "Validation.MissingFields",
        "One or more required fields are missing. Please ensure all mandatory information is provided.");

    public static readonly Error InvalidHost = Error.Problem(
        "Validation.InvalidHost",
        "The address must be an IP address (for example 192.168.0.10) or a hostname (for example nas.local).");
}
