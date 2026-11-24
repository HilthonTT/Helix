using FluentAssertions;
using Helix.Infrastructure.Cryptography;
using System.Security.Cryptography;

namespace Infrastructure.UnitTests.Cryptography;

public sealed class PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_Should_WriteTheCurrentFormat()
    {
        string hash = _hasher.Hash(Password);

        hash.Should().MatchRegex(@"^v2\$600000\$[0-9A-F]{64}\$[0-9A-F]{32}$");
    }

    [Fact]
    public void Hash_Should_SaltEveryHash()
    {
        _hasher.Hash(Password).Should().NotBe(_hasher.Hash(Password));
    }

    [Fact]
    public void Verify_Should_AcceptThePasswordItHashed()
    {
        string hash = _hasher.Hash(Password);

        _hasher.Verify(Password, hash).Should().BeTrue();
        _hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("correct horse battery stapler")]
    [InlineData("Correct horse battery staple")]
    [InlineData("")]
    public void Verify_Should_RefuseAnyOtherPassword(string attempt)
    {
        string hash = _hasher.Hash(Password);

        _hasher.Verify(attempt, hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_RefuseATamperedHash()
    {
        string hash = _hasher.Hash(Password);
        int index = hash.LastIndexOf('$') - 1;
        char flipped = hash[index] == '0' ? '1' : '0';
        string tampered = string.Concat(hash.AsSpan(0, index), flipped.ToString(), hash.AsSpan(index + 1));

        _hasher.Verify(Password, tampered).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_AcceptALegacyHash_AndAskForARehash()
    {
        string legacy = LegacyHash(Password);

        _hasher.Verify(Password, legacy).Should().BeTrue();
        _hasher.Verify("wrong", legacy).Should().BeFalse();
        _hasher.NeedsRehash(legacy).Should().BeTrue();
    }

    [Fact]
    public void Verify_Should_HonourTheIterationsStoredInTheHash_AndAskForARehashBelowTheCurrentCount()
    {
        string weaker = V2Hash(Password, iterations: 1_000);

        _hasher.Verify(Password, weaker).Should().BeTrue();
        _hasher.NeedsRehash(weaker).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v2$")]
    [InlineData("v2$600000$00")]
    [InlineData("v2$600000$00$00$00")]
    [InlineData("v2$abc$00$00")]
    [InlineData("v2$0$00$00")]
    [InlineData("v2$-5$00$00")]
    [InlineData("v2$600000$ZZ$ZZ")]
    [InlineData("no-separator-here")]
    [InlineData("nothex-nothex")]
    public void Verify_Should_RefuseAMalformedHash(string passwordHash)
    {
        _hasher.Verify(Password, passwordHash).Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_RefuseAHashOrSaltOfTheWrongLength()
    {
        string shortHash = $"v2$1000${Convert.ToHexString(new byte[16])}${Convert.ToHexString(new byte[16])}";
        string shortSalt = $"v2$1000${Convert.ToHexString(new byte[32])}${Convert.ToHexString(new byte[8])}";

        _hasher.Verify(Password, shortHash).Should().BeFalse();
        _hasher.Verify(Password, shortSalt).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NeedsRehash_Should_BeFalse_WhenThereIsNoHash(string passwordHash)
    {
        _hasher.NeedsRehash(passwordHash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_Should_BeTrue_ForAnUnreadableV2Hash()
    {
        _hasher.NeedsRehash("v2$garbage").Should().BeTrue();
    }

    private static string LegacyHash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA512, 32);

        return $"{Convert.ToHexString(hash)}-{Convert.ToHexString(salt)}";
    }

    private static string V2Hash(string password, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, 32);

        return $"v2${iterations}${Convert.ToHexString(hash)}${Convert.ToHexString(salt)}";
    }
}
