using FluentAssertions;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;

namespace Infrastructure.UnitTests.Updates;

public sealed class ReleaseSignatureTests
{
    private static readonly byte[] ArchiveHash = SHA256.HashData("a release archive"u8.ToArray());

    private static (string PublicKey, string Signature) Sign(byte[] hash)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return (
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(signer.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence)));
    }

    [Fact]
    public void Verify_Should_Accept_TheSignatureOverTheArchiveThatCameDown()
    {
        (string publicKey, string signature) = Sign(ArchiveHash);

        ReleaseSignature.Verify(ArchiveHash, signature, publicKey, NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void Verify_Should_Refuse_ASignatureOverDifferentBytes()
    {
        (string publicKey, string signature) = Sign(SHA256.HashData("another archive"u8.ToArray()));

        ReleaseSignature.Verify(ArchiveHash, signature, publicKey, NullLogger.Instance).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_Refuse_ASignatureFromAnotherKey()
    {
        (_, string signature) = Sign(ArchiveHash);
        (string publicKey, _) = Sign(ArchiveHash);

        ReleaseSignature.Verify(ArchiveHash, signature, publicKey, NullLogger.Instance).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all !!")]
    public void Verify_Should_Refuse_ASignatureItCannotRead(string signature)
    {
        (string publicKey, _) = Sign(ArchiveHash);

        ReleaseSignature.Verify(ArchiveHash, signature, publicKey, NullLogger.Instance).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_Pass_WhenThisBuildPinsNoKey()
    {
        // What every build published before the workflow began signing has to keep doing.
        ReleaseSignature.Verify(ArchiveHash, string.Empty, string.Empty, NullLogger.Instance).Should().BeTrue();
    }

    [Fact]
    public void FindSignature_Should_ReturnTheLineForThatAsset()
    {
        const string manifest = """
            Helix-v2.2.4-win-arm64.zip AAAA
            Helix-v2.2.4-win-x64.zip BBBB
            Helix-v2.2.4-macos.zip CCCC
            """;

        ReleaseSignature.FindSignature(manifest, "Helix-v2.2.4-win-x64.zip").Should().Be("BBBB");
    }

    [Fact]
    public void FindSignature_Should_ReturnNothing_WhenThatAssetIsNotSigned()
    {
        const string manifest = "Helix-v2.2.4-win-arm64.zip AAAA";

        ReleaseSignature.FindSignature(manifest, "Helix-v2.2.4-win-x64.zip").Should().BeNull();
    }

    [Fact]
    public void FindSignature_Should_SkipBlankAndMalformedLines()
    {
        string manifest = string.Join(
            Environment.NewLine,
            "",
            "# a comment",
            "malformed",
            "Helix-v2.2.4-win-x64.zip BBBB");

        ReleaseSignature.FindSignature(manifest, "Helix-v2.2.4-win-x64.zip").Should().Be("BBBB");
    }

    [Fact]
    public void FindSignature_Should_MatchTheAssetNameExactly()
    {
        // A prefix match would let "Helix-v2.2.4-win-x64.zip" be answered by the arm64 line.
        const string manifest = "Helix-v2.2.4-win-x64.zip.old AAAA";

        ReleaseSignature.FindSignature(manifest, "Helix-v2.2.4-win-x64.zip").Should().BeNull();
    }
}
