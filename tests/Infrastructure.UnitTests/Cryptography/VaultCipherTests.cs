using FluentAssertions;
using Helix.Application.Core.Errors;
using Helix.Infrastructure.Cryptography;
using System.Text.Json.Nodes;

namespace Infrastructure.UnitTests.Cryptography;

public sealed class VaultCipherTests
{
    private const string Passphrase = "a long enough passphrase";
    private const string Plaintext = "{\"drives\":[{\"letter\":\"Z\",\"name\":\"Médiathèque ファイル\"}]}";

    private readonly VaultCipher _cipher = new();

    [Fact]
    public void Decrypt_Should_ReturnWhatWasEncrypted()
    {
        string vault = _cipher.Encrypt(Plaintext, Passphrase);

        Result<string> result = _cipher.Decrypt(vault, Passphrase);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Plaintext);
    }

    [Fact]
    public void Encrypt_Should_WriteTheVersionOneEnvelope_WithoutThePlaintext()
    {
        string vault = _cipher.Encrypt(Plaintext, Passphrase);
        JsonObject envelope = JsonNode.Parse(vault)!.AsObject();

        envelope["v"]!.GetValue<int>().Should().Be(1);
        envelope["kdf"]!.GetValue<string>().Should().Be("PBKDF2-SHA512");
        envelope["iters"]!.GetValue<int>().Should().Be(600_000);
        Convert.FromBase64String(envelope["salt"]!.GetValue<string>()).Should().HaveCount(16);
        Convert.FromBase64String(envelope["nonce"]!.GetValue<string>()).Should().HaveCount(12);
        Convert.FromBase64String(envelope["tag"]!.GetValue<string>()).Should().HaveCount(16);
        vault.Should().NotContain("drives");
    }

    [Fact]
    public void Encrypt_Should_NeverRepeatSaltNonceOrCiphertext()
    {
        JsonObject first = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();
        JsonObject second = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();

        second["salt"]!.GetValue<string>().Should().NotBe(first["salt"]!.GetValue<string>());
        second["nonce"]!.GetValue<string>().Should().NotBe(first["nonce"]!.GetValue<string>());
        second["ct"]!.GetValue<string>().Should().NotBe(first["ct"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Encrypt_Should_Throw_WithoutAPassphrase(string passphrase)
    {
        Action encrypt = () => _cipher.Encrypt(Plaintext, passphrase);

        encrypt.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_Should_Fail_WithTheWrongPassphrase()
    {
        string vault = _cipher.Encrypt(Plaintext, Passphrase);

        Result<string> result = _cipher.Decrypt(vault, Passphrase + "!");

        result.Error.Should().Be(JsonErrors.VaultDecryptFailed);
    }

    [Theory]
    [InlineData("ct")]
    [InlineData("tag")]
    [InlineData("nonce")]
    [InlineData("salt")]
    public void Decrypt_Should_Fail_WhenAnyPartWasTamperedWith(string field)
    {
        JsonObject envelope = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();
        byte[] bytes = Convert.FromBase64String(envelope[field]!.GetValue<string>());
        bytes[0] ^= 0x01;
        envelope[field] = Convert.ToBase64String(bytes);

        Result<string> result = _cipher.Decrypt(envelope.ToJsonString(), Passphrase);

        result.Error.Should().Be(JsonErrors.VaultDecryptFailed);
    }

    [Theory]
    [InlineData("v", 2)]
    [InlineData("iters", 0)]
    [InlineData("iters", 5_000_001)]
    public void Decrypt_Should_RefuseAnEnvelopeItCannotRead(string field, int value)
    {
        JsonObject envelope = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();
        envelope[field] = value;

        Result<string> result = _cipher.Decrypt(envelope.ToJsonString(), Passphrase);

        result.Error.Should().Be(JsonErrors.VaultUnsupportedVersion);
    }

    [Fact]
    public void Decrypt_Should_RefuseAnotherKeyDerivation()
    {
        JsonObject envelope = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();
        envelope["kdf"] = "scrypt";

        Result<string> result = _cipher.Decrypt(envelope.ToJsonString(), Passphrase);

        result.Error.Should().Be(JsonErrors.VaultUnsupportedVersion);
    }

    [Theory]
    [InlineData("salt", "not base64!")]
    [InlineData("salt", "AAAA")]
    [InlineData("nonce", "AAAA")]
    [InlineData("tag", "AAAA")]
    public void Decrypt_Should_Fail_WhenAFieldIsUnreadableOrTheWrongSize(string field, string value)
    {
        JsonObject envelope = JsonNode.Parse(_cipher.Encrypt(Plaintext, Passphrase))!.AsObject();
        envelope[field] = value;

        Result<string> result = _cipher.Decrypt(envelope.ToJsonString(), Passphrase);

        result.Error.Should().Be(JsonErrors.VaultDecryptFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Decrypt_Should_Fail_ForSomethingThatIsNotAVault(string payload)
    {
        Result<string> result = _cipher.Decrypt(payload, Passphrase);

        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Decrypt_Should_Fail_WithoutAPassphrase(string passphrase)
    {
        string vault = _cipher.Encrypt(Plaintext, Passphrase);

        Result<string> result = _cipher.Decrypt(vault, passphrase);

        result.Error.Should().Be(JsonErrors.VaultDecryptFailed);
    }
}
