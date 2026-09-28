using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Core.Security;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Cryptography;

internal sealed class RecoveryKeyGenerator : IRecoveryKeyGenerator
{
    public string Generate()
    {
        string characters = RandomNumberGenerator.GetString(RecoveryKeyFormat.Alphabet, RecoveryKeyFormat.Length);

        return RecoveryKeyFormat.Format(characters);
    }
}
