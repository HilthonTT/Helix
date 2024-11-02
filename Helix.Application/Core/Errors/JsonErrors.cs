using SharedKernel;

namespace Helix.Application.Core.Errors;

internal static class JsonErrors
{
    public static readonly Error Invalid = Error.Problem(
        "Json.Invalid", "Your json format is invalid.");
}
