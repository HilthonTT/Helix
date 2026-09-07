using System.Text.Json.Serialization;

namespace Helix.Application.Features.Drives.Contracts;

public sealed record DriveImportDto(
    string Letter,
    string Host,
    string Name,
    string Username,
    string Password,
    bool AutoConnect = true,
    bool Persistent = false,
    bool ConnectByHostname = false)
{
    [JsonPropertyName("IpAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyIpAddress { get; init; }

    [JsonIgnore]
    public string EffectiveHost => string.IsNullOrWhiteSpace(Host) ? LegacyIpAddress ?? string.Empty : Host;
}
