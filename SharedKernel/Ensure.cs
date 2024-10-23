using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharedKernel;

public static class Ensure
{
    public static void NotNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression("value")] string? paramName = default)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void NotNullOrEmpty(
        [NotNull] Guid value,
        [CallerArgumentExpression("value")] string? paramName = default)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void NotNull(
        [NotNull] object? value,
        [CallerArgumentExpression("value")] string? paramName = default)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void MustBeOneChar(
        [NotNull] string? value,
        [CallerArgumentExpression("value")] string? paramName = default)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.Length > 1)
        {
            throw new InvalidOperationException(paramName);
        }
    }

    public static void MustBePositive(
        int value, 
        [CallerArgumentExpression("value")] string? paramName = default)
    {
        if (!int.IsPositive(value))
        {
            throw new InvalidOperationException(paramName);
        }
    }
}
