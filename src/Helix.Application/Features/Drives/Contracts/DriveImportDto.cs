using System.Text.Json.Serialization;

namespace Helix.Application.Features.Drives.Contracts;

/// <summary>
/// DTO used as the JSON shape for drive export/import. Carries only the fields the
/// user actually owns; identity (<c>Id</c>, <c>UserId</c>) and audit timestamps are
/// re-issued at import time so a tampered file cannot forge primary keys or claim
/// drives across users.
/// </summary>
/// <param name="AutoConnect">
/// Defaults to true, so a vault written before the flag existed imports as drives that
/// take part in the unattended passes — which is what they did when it was exported.
/// </param>
public sealed record DriveImportDto(
    string Letter,
    string Host,
    string Name,
    string Username,
    string Password,
    bool AutoConnect = true,
    bool Persistent = false)
{
    /// <summary>
    /// The host under its former name, read from vaults written before the field was
    /// renamed.
    /// </summary>
    /// <remarks>
    /// A <c>.helixvault</c> file lives on the user's disk indefinitely and is the only
    /// copy of credentials they may still have, so a rename inside Helix must not make
    /// one unreadable. Old files carry <c>IpAddress</c> and no <c>Host</c>; new ones are
    /// written with <c>Host</c> only, and this stays absent rather than serialising null.
    /// </remarks>
    [JsonPropertyName("IpAddress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyIpAddress { get; init; }

    /// <summary>The host to import, from whichever of the two field names carried it.</summary>
    [JsonIgnore]
    public string EffectiveHost => string.IsNullOrWhiteSpace(Host) ? LegacyIpAddress ?? string.Empty : Host;
}
