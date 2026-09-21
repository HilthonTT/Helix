namespace Helix.Domain.Drives;

public static class MacAddresses
{
    private const int Octets = 6;

    public static bool IsValid(string? candidate) => TryNormalize(candidate, out _);

    public static string? Normalize(string? candidate) =>
        TryNormalize(candidate, out string? normalized) ? normalized : null;

    public static byte[]? ToBytes(string? candidate)
    {
        if (!TryNormalize(candidate, out string? normalized))
        {
            return null;
        }

        byte[] bytes = new byte[Octets];

        for (int i = 0; i < Octets; i++)
        {
            bytes[i] = Convert.ToByte(normalized!.Substring(i * 3, 2), 16);
        }

        return bytes;
    }

    private static bool TryNormalize(string? candidate, out string? normalized)
    {
        normalized = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        Span<char> digits = stackalloc char[Octets * 2];
        int count = 0;

        foreach (char character in candidate)
        {
            if (character is '-' or ':' or '.' or ' ')
            {
                continue;
            }

            if (!Uri.IsHexDigit(character) || count == digits.Length)
            {
                return false;
            }

            digits[count++] = char.ToLowerInvariant(character);
        }

        if (count != digits.Length)
        {
            return false;
        }

        Span<char> formatted = stackalloc char[(Octets * 3) - 1];

        for (int i = 0; i < Octets; i++)
        {
            if (i > 0)
            {
                formatted[(i * 3) - 1] = '-';
            }

            formatted[i * 3] = digits[i * 2];
            formatted[(i * 3) + 1] = digits[(i * 2) + 1];
        }

        normalized = new string(formatted);

        return true;
    }
}
