using FluentAssertions;
using Helix.Application.Features.Drives.Contracts;
using System.Text.Json;

namespace Application.UnitTests.Features.Drives.Contracts;

/// <summary>
/// Guards the vault format across the <c>IpAddress</c> to <c>Host</c> rename.
/// </summary>
/// <remarks>
/// A <c>.helixvault</c> file sits on the user's disk indefinitely and may be the only
/// copy of a set of credentials they still have. A rename inside Helix that made one
/// unreadable would be data loss, and the failure would only show up on the day someone
/// needed to restore.
/// </remarks>
public sealed class DriveImportDtoTests
{
    private const string LegacyVaultEntry = """
        {
            "Letter": "Z",
            "IpAddress": "192.168.0.10",
            "Name": "Media",
            "Username": "user",
            "Password": "password"
        }
        """;

    private static DriveImportDto Deserialize(string json) =>
        JsonSerializer.Deserialize<DriveImportDto>(json)!;

    [Fact]
    public void EffectiveHost_Should_ReadTheOldFieldName_FromAVaultWrittenBeforeTheRename()
    {
        DriveImportDto dto = Deserialize(LegacyVaultEntry);

        dto.EffectiveHost.Should().Be("192.168.0.10");
    }

    [Fact]
    public void AutoConnect_Should_DefaultToTrue_ForAVaultWrittenBeforeTheFlagExisted()
    {
        DriveImportDto dto = Deserialize(LegacyVaultEntry);

        // Those drives connected on startup when they were exported; importing them as
        // opted-out would quietly change what the backup restores to.
        dto.AutoConnect.Should().BeTrue();
        dto.Persistent.Should().BeFalse();
    }

    [Fact]
    public void EffectiveHost_Should_PreferTheCurrentFieldName()
    {
        DriveImportDto dto = Deserialize("""
            {
                "Letter": "Z",
                "Host": "nas.local",
                "IpAddress": "192.168.0.10",
                "Name": "Media",
                "Username": "user",
                "Password": "password"
            }
            """);

        dto.EffectiveHost.Should().Be("nas.local");
    }

    [Fact]
    public void Serialization_Should_WriteHostAndOmitTheLegacyField()
    {
        var dto = new DriveImportDto("Z", "nas.local", "Media", "user", "password");

        string json = JsonSerializer.Serialize(dto);

        json.Should().Contain("\"Host\":\"nas.local\"");
        json.Should().NotContain("IpAddress");
    }

    [Fact]
    public void Serialization_Should_RoundTrip()
    {
        var dto = new DriveImportDto("Z", "fd00::5", "Media", "user", "password", AutoConnect: false, Persistent: true);

        DriveImportDto restored = Deserialize(JsonSerializer.Serialize(dto));

        restored.EffectiveHost.Should().Be("fd00::5");
        restored.AutoConnect.Should().BeFalse();
        restored.Persistent.Should().BeTrue();
    }
}
