using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Utils;

/// <summary>
///     Full-path glob matching for a set of rules. <c>*</c> stays inside one name, <c>**</c>
///     crosses folders and <c>?</c> matches one character, so the platform-specific path behaviour
///     can be tested on its own. A rule without a folder part (such as <c>*.log</c>) applies to
///     that name at any depth. Environment variables, <c>$VAR</c> and <c>~</c> are expanded before
///     matching.
/// </summary>
[PublicAPI]
public static partial class PathGlob
{
    /// <summary>Whether a rule matches the target, or the target is a parent of the rule's base.</summary>
    public static bool Covers(string target, string pattern)
    {
        target = Normalize(target);
        if (target.Length == 0)
            return false;

        var (glob, basePath) = ParseRule(pattern);
        if (glob.Length == 0)
            return false;

        if (GlobMatch(target, glob))
            return true;

        // A folder-scoped rule with a "**" can match somewhere below a folder that is itself neither
        // a match nor a parent of the base. Deleting such a folder would take those matches with it.
        if (basePath.Length > 0 && CouldContainMatch(target, glob))
            return true;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Deleting a parent would take the protected base down with it.
        return basePath.Length > 0
               && (basePath.Equals(target, comparison)
                   || basePath.StartsWith(WithSeparator(target), comparison));
    }

    /// <summary>Expands environment variables and "~", then makes the path absolute and canonical.</summary>
    public static string Normalize(string path)
    {
        var value = Expand(path.Trim().Trim('"'));
        if (value.Length == 0)
            return "";

        try
        {
            value = Path.GetFullPath(value);
        }
        catch
        {
            return "";
        }

        var root = Path.GetPathRoot(value);
        if (root is { Length: > 0 } && value.Equals(root, StringComparison.OrdinalIgnoreCase))
            return value;

        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    ///     Folders where a stray delete would be expensive. The defaults stay in environment-variable
    ///     form, so they follow the profile and system drive instead of freezing one machine's paths.
    /// </summary>
    public static List<string> Defaults()
    {
        var paths = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            paths.Add(@"%SystemRoot%\**");
            paths.Add(@"%USERPROFILE%\*");
            paths.Add(@"%ProgramFiles%\*");

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ProgramFiles(x86)")))
                paths.Add(@"%ProgramFiles(x86)%\*");

            paths.Add(@"%ProgramData%\*");
        }
        else
        {
            foreach (var folder in new[]
                     {
                         "/boot", "/etc", "/usr", "/var", "/dev", "/proc", "/sys", "/bin", "/sbin", "/lib", "/lib64"
                     })
                paths.Add($"{folder}/**");

            paths.Add(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME"))
                ? $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/*"
                : "$HOME/*");
        }

        return paths;
    }

    /// <summary>
    ///     Normalizes the literal part of a rule and rebuilds the glob with native separators. A rule
    ///     without a folder part is widened to "any depth"; the literal base is used for the
    ///     "don't delete its parent" check.
    /// </summary>
    private static (string Glob, string BasePath) ParseRule(string raw)
    {
        var expanded = Expand(raw.Trim().Trim('"'));
        if (expanded.Length == 0)
            return ("", "");

        var separator = Path.DirectorySeparatorChar;

        if (OperatingSystem.IsWindows())
            expanded = expanded.Replace(Path.AltDirectorySeparatorChar, separator);

        var wildcard = expanded.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            var exact = Normalize(expanded);
            return exact.Length == 0 ? ("", "") : (exact, exact);
        }

        var prefix = expanded[..wildcard];
        var remainder = expanded[wildcard..];

        if (prefix.Length == 0)
        {
            // No folder part, so the name matches at any depth: "*.log" becomes "**\*.log".
            var glob = remainder.StartsWith("**", StringComparison.Ordinal)
                ? remainder
                : $"**{separator}{remainder}";
            return (glob, "");
        }

        var endsWithSeparator = prefix.EndsWith(separator);
        var normalized = Normalize(prefix);
        if (normalized.Length == 0)
            return ("", "");

        var join = endsWithSeparator && !normalized.EndsWith(separator) ? separator.ToString() : "";
        return ($"{normalized}{join}{remainder}", normalized);
    }

    /// <summary>
    ///     Whether the pattern could match something below a real directory target. Name-only rules
    ///     (base empty) are excluded by the caller, so a global "*.log" never blocks every folder.
    /// </summary>
    private static bool CouldContainMatch(string target, string pattern)
    {
        if (!Directory.Exists(target))
            return false;

        var separator = Path.DirectorySeparatorChar;
        var targetParts = target.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var patternParts = pattern.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return MatchPrefix(targetParts, 0, patternParts, 0, comparison);
    }

    /// <summary>
    ///     Consumes every target segment and reports whether the pattern still has a non-empty
    ///     remainder afterwards, which would mean a match could live below the target.
    /// </summary>
    private static bool MatchPrefix(string[] target, int targetIndex, string[] pattern, int patternIndex,
        StringComparison comparison)
    {
        while (patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == "**")
            {
                for (var skip = targetIndex; skip <= target.Length; skip++)
                    if (MatchPrefix(target, skip, pattern, patternIndex + 1, comparison))
                        return true;

                return false;
            }

            if (targetIndex >= target.Length)
                return true;

            if (!MatchSegment(target[targetIndex], pattern[patternIndex], comparison))
                return false;

            targetIndex++;
            patternIndex++;
        }

        return false;
    }

    /// <summary>Segment-wise glob match; a segment that is exactly "**" spans any number of folders.</summary>
    private static bool GlobMatch(string target, string pattern)
    {
        var separator = Path.DirectorySeparatorChar;
        var targetParts = target.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var patternParts = pattern.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return MatchSegments(targetParts, 0, patternParts, 0, comparison);
    }

    private static bool MatchSegments(string[] target, int targetIndex, string[] pattern, int patternIndex,
        StringComparison comparison)
    {
        while (patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == "**")
            {
                while (patternIndex + 1 < pattern.Length && pattern[patternIndex + 1] == "**")
                    patternIndex++;

                if (patternIndex + 1 == pattern.Length)
                    return true;

                for (var skip = targetIndex; skip <= target.Length; skip++)
                    if (MatchSegments(target, skip, pattern, patternIndex + 1, comparison))
                        return true;

                return false;
            }

            if (targetIndex >= target.Length)
                return false;

            if (!MatchSegment(target[targetIndex], pattern[patternIndex], comparison))
                return false;

            targetIndex++;
            patternIndex++;
        }

        return targetIndex == target.Length;
    }

    /// <summary>Wildcard match inside one path segment, where "*" and "?" never cross a separator.</summary>
    private static bool MatchSegment(string value, string pattern, StringComparison comparison)
    {
        var valueIndex = 0;
        var patternIndex = 0;
        var star = -1;
        var resume = 0;

        while (valueIndex < value.Length)
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?' || SameChar(value[valueIndex], pattern[patternIndex], comparison)))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                star = patternIndex++;
                resume = valueIndex;
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                valueIndex = ++resume;
            }
            else
                return false;

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;

        return patternIndex == pattern.Length;
    }

    private static bool SameChar(char left, char right, StringComparison comparison) =>
        left.Equals(right, comparison);

    private static string Expand(string path)
    {
        var value = Environment.ExpandEnvironmentVariables(path);

        if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal)
                         || value.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            value = home + value[1..];
        }

        return EnvironmentVariable().Replace(value,
            match => Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? match.Value);
    }

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    [GeneratedRegex(@"\$\{?([A-Za-z_][A-Za-z0-9_]*)\}?")]
    private static partial Regex EnvironmentVariable();
}