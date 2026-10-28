using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Extensions;

/// <summary>Small string helpers used when logging raw protocol traffic.</summary>
[PublicAPI]
public static class StringExtensions
{
    /// <summary>Removes control characters, keeping the ones listed in <paramref name="exclude" />.</summary>
    public static string RemoveControlChars(this string value, params char[] exclude) =>
        string.Concat(value.Where(character => !char.IsControl(character) || exclude.Contains(character)));

    /// <summary>
    ///     Returns the value shortened to at most <paramref name="maximumLength" /> characters. The result
    ///     follows the argument's nullability: a non-null input yields a non-null <see cref="string" />.
    /// </summary>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Truncate(this string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}