using System.Globalization;

namespace PhotinoNetUtils.CodeGen;

/// <summary>
///     Runs the settings reader over a small source that exercises every accepted spelling of
///     <c>[Setting]</c>. <c>--self-test</c> exists so a change to the scanner can be checked without
///     building an app, and so the forms the other side (the Roslyn generator) supports stay
///     covered here too.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        var failures = 0;

        foreach (var (name, source, expected) in Cases())
        {
            var file = SettingsSource.Read(source);
            var actual = string.Join(",", file.Properties.Select(Property));

            if (file.Errors.Count > 0)
            {
                Console.Error.WriteLine($"self-test {name}: unexpected error: {file.Errors[0]}");
                failures++;
            }
            else if (actual != expected)
            {
                Console.Error.WriteLine($"self-test {name}: expected [{expected}], got [{actual}]");
                failures++;
            }
        }

        if (Duplicate() is { } duplicate)
        {
            Console.Error.WriteLine($"self-test duplicate: expected one error, got {duplicate}");
            failures++;
        }

        if (failures > 0)
        {
            Console.Error.WriteLine($"{failures} self-test case(s) failed.");
            return 1;
        }

        Console.WriteLine($"{Cases().Length + 1} self-test case(s) passed.");
        return 0;
    }

    private static string Property(SettingProperty property) =>
        $"{property.Key}|{property.Comment}|{Number(property.Min)}|{Number(property.Max)}";

    private static string Number(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>Two settings with one key are an error; the message is the result.</summary>
    private static string? Duplicate()
    {
        var file = SettingsSource.Read("""
            [SettingsModel]
            public sealed record S
            {
                [Setting("one")]
                public string A { get; init; }

                [Setting("two", Key = "a")]
                public string B { get; init; }
            }
            """);

        return file.Errors.Count == 1 ? null : $"{file.Errors.Count} error(s): {string.Join(" / ", file.Errors)}";
    }

    private static (string Name, string Source, string Expected)[] Cases() =>
    [
        ("plain", """
            [SettingsModel]
            public sealed record S
            {
                [Setting(Comment = "one")]
                public string A { get; init; } = "";
            }
            """, "a|one||"),

        ("layout", """
            [SettingsModel]
            public sealed record S
            {
                [global::PhotinoNETUtils.Settings.Setting(
                    Comment = "brackets ] and )] inside",
                    Key = "customKey")]
                public int B { get; init; } = 3;
            }
            """, "customKey|brackets ] and )] inside||"),

        ("positional", """
            [SettingsModel]
            public sealed record S
            {
                [Setting("positional", Min = -5, Max = 1_000)]
                public int C { get; init; }
            }
            """, "c|positional|-5|1000"),

        ("same-line and bare", """
            [SettingsModel]
            public sealed record S
            {
                [Setting]
                public bool D { get; init; }

                [Setting(Comment = "same line")] public string E { get; init; } = "";
            }
            """, "d|||,e|same line||"),

        ("record parameters", """
            [SettingsModel]
            public sealed record R(
                [property: Setting("first")] string X = "1",
                [property: Setting(Comment = "second", Min = 0.5f)] double Y = 0);
            """, "x|first||,y|second|0.5|"),

        ("constants", """
            public static class Keys
            {
                public const string Folder = "log" + "Directory";
                public const int Low = 1;
            }

            [SettingsModel]
            public sealed record S
            {
                [Setting(Comment = nameof(S), Key = Keys.Folder, Min = Keys.Low)]
                public int A { get; init; }
            }
            """, "logDirectory|S|1|"),

        ("members", """
            [SettingsModel]
            public sealed record S
            {
                [Setting("keyword", Min = 0, Max = 10)]
                public int @class { get; init; }

                [Setting(Comment = "static")]
                public static string Shared { get; } = "";

                [Setting(Comment = "write only")]
                public string Hidden { set { } }

                [Setting(Comment = "expression")]
                public string Computed => "";
            }
            """, "class|keyword|0|10"),

        ("raw string", """"
            [SettingsModel]
            public sealed record S
            {
                [Setting(Comment = """raw "text" here""")]
                public string R { get; init; } = "";
            }
            """", "r|raw \"text\" here||")
    ];
}
