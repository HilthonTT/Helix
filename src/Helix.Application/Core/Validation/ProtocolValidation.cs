using Helix.Application.Core.Errors;
using Helix.Domain.Drives;

namespace Helix.Application.Core.Validation;

internal static class ProtocolValidation
{
    internal static Result Validate(
        DriveProtocol protocol,
        int? port,
        string letter,
        string host,
        string name,
        string? username,
        string? password)
    {
        if (!DriveProtocols.IsKnown(protocol))
        {
            return Result.Failure(DriveErrors.UnknownProtocol);
        }

        if (!DriveProtocols.IsValidPort(port))
        {
            return Result.Failure(DriveErrors.InvalidPort);
        }

        List<string?> properties = [letter, host, name];

        if (protocol.UsesCredentials())
        {
            properties.Add(username);
            properties.Add(password);
        }

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
