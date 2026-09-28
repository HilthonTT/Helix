using System.Text;

namespace Helix.Application.Core.Security;

public static class RecoveryKeyFormat
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 25;

    public const int GroupSize = 5;

    public const char Separator = '-';

    public static string Format(ReadOnlySpan<char> characters)
    {
        var builder = new StringBuilder(characters.Length + characters.Length / GroupSize);

        for (int i = 0; i < characters.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                builder.Append(Separator);
            }

            builder.Append(characters[i]);
        }

        return builder.ToString();
    }

    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var builder = new StringBuilder(Length);

        foreach (char raw in input)
        {
            if (raw == Separator || char.IsWhiteSpace(raw))
            {
                continue;
            }

            char c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                char other => other,
            };

            if (!Alphabet.Contains(c))
            {
                return null;
            }

            builder.Append(c);
        }

        return builder.Length == Length ? Format(builder.ToString()) : null;
    }
}
