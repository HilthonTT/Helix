using SharedKernel;

namespace Helix.Application.Core.Errors;

public static class JsonErrors
{
    public static readonly Error Invalid = Error.Problem(
        "Json.Invalid", "Your valid is valid.");
}
