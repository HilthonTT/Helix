using SharedKernel;

namespace Helix.Application.Core.Errors;

internal static class ValidationErrors
{
    public static readonly Error MissingFields = Error.Problem(
        "Validation.MissingFields",
         "One or more required fields are missing. Please ensure all mandatory information is provided.");
}
