using FluentAssertions;
using Helix.Infrastructure.Cryptography;

namespace Infrastructure.UnitTests.Cryptography;

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generated_Password_Should_Be_Random_Enough()
    {
        // Arrange
        const int passwordLength = 128;
        const int sampleSize = 100;
        var generatedPasswords = new HashSet<string>();

        bool hasUpperCase = false;
        bool hasLowerCase = false;
        bool hasDigit = false;
        bool hasSpecialChar = false;

        // Act
        for (int i = 0; i < sampleSize; i++)
        {
            string password = PasswordGenerator.GetOrCreatePassword();

            // Add to set to test uniqueness
            generatedPasswords.Add(password);

            // Check password complexity in the first generated password
            if (i == 0)
            {
                hasUpperCase = password.Any(char.IsUpper);
                hasLowerCase = password.Any(char.IsLower);
                hasDigit = password.Any(char.IsDigit);
                hasSpecialChar = password.Any("!@$?_-".Contains);
            }
        }

        generatedPasswords.Should().HaveCount(sampleSize, "each generated password should be unique");
        generatedPasswords.Should().OnlyContain(p => p.Length == passwordLength, "each password should have the correct length");

        hasUpperCase.Should().BeTrue("the password should contain at least one uppercase letter");
        hasLowerCase.Should().BeTrue("the password should contain at least one lowercase letter");
        hasDigit.Should().BeTrue("the password should contain at least one digit");
        hasSpecialChar.Should().BeTrue("the password should contain at least one special character");
    }
}
