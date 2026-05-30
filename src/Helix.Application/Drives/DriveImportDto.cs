namespace Helix.Application.Drives;

/// <summary>
/// DTO used as the JSON shape for drive export/import. Carries only the fields the
/// user actually owns; identity (<c>Id</c>, <c>UserId</c>) and audit timestamps are
/// re-issued at import time so a tampered file cannot forge primary keys or claim
/// drives across users.
/// </summary>
public sealed record DriveImportDto(
    string Letter,
    string IpAddress,
    string Name,
    string Username,
    string Password);
