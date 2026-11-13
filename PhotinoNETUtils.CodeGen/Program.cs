using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotinoNetUtils.CodeGen;

/// <summary>
///     Reads a C# settings model and writes the matching TypeScript interface and JSON schema.
///     Syntax-only on purpose: it runs before the UI build and must not need the app's assemblies.
/// </summary>
internal static partial class Program
{
    private static readonly Dictionary<string, string> ClrNames = new(StringComparer.Ordinal)
    {
        ["Boolean"] = "bool",
        ["Byte"] = "byte",
        ["SByte"] = "sbyte",
        ["Int16"] = "short",
        ["UInt16"] = "ushort",
        ["Int32"] = "int",
        ["UInt32"] = "uint",
        ["Int64"] = "long",
        ["UInt64"] = "ulong",
        ["Single"] = "float",
        ["Double"] = "double",
        ["Decimal"] = "decimal",
        ["Char"] = "char",
        ["String"] = "string",
        ["Object"] = "object"
    };

    private static int Main(string[] args)
    {
        var options = Parse(args);
        if (options is null)
            return 1;

        if (options is { Assembly: { } assemblyPath, Output: { } outputPath })
        {
            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
                return 1;
            }

            SettingsFile settings;

            try
            {
                settings = AssemblySettings.Read(assemblyPath);
            }
            catch (Exception exception) when (exception is BadImageFormatException or IOException)
            {
                Console.Error.WriteLine($"Could not read {assemblyPath}: {exception.Message}");
                return 1;
            }

            if (WriteSettings(settings, outputPath, options.Schema, Path.GetFileNameWithoutExtension(assemblyPath),
                    assemblyPath) is { } code)
                return code;
        }

        if (options is { I18n: { } i18nPath, I18nOutput: { } keysPath })
        {
            if (!File.Exists(i18nPath))
            {
                Console.Error.WriteLine($"Translation file not found: {i18nPath}");
                return 1;
            }

            var text = File.ReadAllText(i18nPath);

            // Keys are case-insensitive, so two spellings of one key are a mistake; the app keeps
            // the first one. Fail here as well, so the UI build never disagrees with the backend.
            if (DuplicateKey(text) is { } duplicate)
            {
                Console.Error.WriteLine($"Duplicate localization key {duplicate}; keys are case-insensitive. " +
                                        "Rename or remove the duplicate.");
                return 1;
            }

            var keys = Keys(text);

            if (keys.Count == 0)
            {
                Console.Error.WriteLine($"No keys found in {i18nPath}");
                return 1;
            }

            WriteFile(keysPath, KeysTypeScript(Path.GetFileName(i18nPath), keys));
            Console.WriteLine($"{keysPath}: {keys.Count} key(s)");
        }

        return 0;
    }

    /// <summary>Writes the interface and schema, or explains why the model cannot be used.</summary>
    private static int? WriteSettings(
        SettingsFile settings,
        string outputPath,
        string? schemaPath,
        string fallbackName,
        string source)
    {
        // Two settings with one key would give the interface one property for two backend values,
        // which is always a mistake in the model.
        if (settings.Errors.Count > 0)
        {
            foreach (var error in settings.Errors)
                Console.Error.WriteLine(error);

            return 1;
        }

        if (settings.Properties.Count == 0)
        {
            Console.Error.WriteLine($"No [Setting] properties found in {source}");
            return 1;
        }

        var modelName = settings.ModelName ?? fallbackName;

        WriteFile(outputPath, TypeScript(modelName, settings.Properties));
        Console.WriteLine($"{outputPath}: {settings.Properties.Count} setting(s)");

        if (schemaPath is { Length: > 0 })
        {
            WriteFile(schemaPath, Schema(modelName, settings.Properties));
            Console.WriteLine($"{schemaPath}: JSON schema");
        }

        return null;
    }

    private static Options? Parse(string[] args)
    {
        string? assembly = null;
        string? output = null;
        string? schema = null;
        string? i18n = null;
        string? i18nOutput = null;

        for (var index = 0; index < args.Length; index++)
        {
            var next = index + 1 < args.Length ? args[index + 1] : null;

            switch (args[index])
            {
                case "--assembly" when next is not null:
                    assembly = Path.GetFullPath(next);
                    break;
                case "--out" when next is not null:
                    output = Path.GetFullPath(next);
                    break;
                case "--schema" when next is not null:
                    schema = Path.GetFullPath(next);
                    break;
                case "--i18n" when next is not null:
                    i18n = Path.GetFullPath(next);
                    break;
                case "--i18n-out" when next is not null:
                    i18nOutput = Path.GetFullPath(next);
                    break;
                default:
                    Console.Error.WriteLine(
                        "Usage: PhotinoNETUtils.CodeGen --assembly <App.dll> --out <settings.generated.ts> " +
                        "[--schema <settings.schema.json>] [--i18n <en.jsonc> --i18n-out <i18n.generated.ts>]");
                    return null;
            }

            index++;
        }

        if (assembly is null != output is null)
        {
            Console.Error.WriteLine("--assembly and --out belong together.");
            return null;
        }

        if (i18n is null != i18nOutput is null)
        {
            Console.Error.WriteLine("--i18n and --i18n-out belong together.");
            return null;
        }

        if (assembly is null && i18n is null)
        {
            Console.Error.WriteLine("Nothing to generate: pass --assembly with --out, or --i18n/--i18n-out.");
            return null;
        }

        return new(assembly, output, schema, i18n, i18nOutput);
    }

    private static string TypeScript(string modelName, List<SettingProperty> properties)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine($"// Generated from the {modelName} settings model; edit the C# model, not this file.");
        builder.AppendLine();
        builder.AppendLine($"export interface {modelName}Generated {{");

        foreach (var property in properties)
        {
            if (property.Comment is { Length: > 0 } comment)
                builder.AppendLine($"    /** {comment.Replace("*/", "* /")} */");

            builder.AppendLine($"    {TsName(property.Key)}: {TsType(property.Type)};");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string Schema(string modelName, List<SettingProperty> properties)
    {
        var schemaProperties = new Dictionary<string, object?>();

        foreach (var property in properties)
        {
            var schema = JsonType(property.Type);

            if (property.Comment is { Length: > 0 } comment)
                schema["description"] = comment;

            if (property.Min is { } min)
                schema["minimum"] = min;

            if (property.Max is { } max)
                schema["maximum"] = max;

            schemaProperties[property.Key] = schema;
        }

        var root = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = modelName,
            ["type"] = "object",
            ["properties"] = schemaProperties,
            ["additionalProperties"] = false
        };

        return WriteJson(root, 0) + Environment.NewLine;
    }

    /// <summary>
    ///     Writes the schema by hand. The generator is published as native AOT, where the reflection
    ///     based <c>JsonSerializer</c> would either warn or need the whole serializer metadata; the
    ///     shapes here are a handful of dictionaries, strings and numbers.
    /// </summary>
    private static string WriteJson(object? value, int depth) =>
        value switch
        {
            null => "null",
            string text => Quote(text),
            bool flag => flag ? "true" : "false",
            int or long or double or float or decimal =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            Dictionary<string, object?> map => WriteObject(map, depth),
            IEnumerable<object?> items => WriteArray(items, depth),
            _ => Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
        };

    private static string WriteObject(Dictionary<string, object?> map, int depth)
    {
        if (map.Count == 0)
            return "{}";

        var indent = new string(' ', (depth + 1) * 2);
        var closing = new string(' ', depth * 2);
        var entries = map.Select(pair => $"{indent}{Quote(pair.Key)}: {WriteJson(pair.Value, depth + 1)}");

        return "{\n" + string.Join(",\n", entries) + "\n" + closing + "}";
    }

    private static string WriteArray(IEnumerable<object?> items, int depth)
    {
        var values = items.ToList();
        if (values.Count == 0)
            return "[]";

        var indent = new string(' ', (depth + 1) * 2);
        var closing = new string(' ', depth * 2);
        var entries = values.Select(item => indent + WriteJson(item, depth + 1));

        return "[\n" + string.Join(",\n", entries) + "\n" + closing + "]";
    }

    /// <summary>A JSON/JavaScript string literal, so the schema and the TS union share one escaper.</summary>
    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var character in value)
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (character < ' ')
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(character);
                    break;
            }

        builder.Append('"');
        return builder.ToString();
    }

    private static string TsName(string key) =>
        IdentifierPattern().IsMatch(key) ? key : Quote(key);

    private static List<string> Keys(string text) =>
    [
        .. KeyLinePattern().Matches(text)
            .Select(match => Regex.Unescape(match.Groups["key"].Value))
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key, StringComparer.Ordinal)
    ];

    /// <summary>Returns a description of the first key declared twice, ignoring case, or null.</summary>
    private static string? DuplicateKey(string text)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var line = 1;
        var previous = 0;

        foreach (Match match in KeyLinePattern().Matches(text))
        {
            for (var index = previous; index < match.Index; index++)
                if (text[index] == '\n')
                    line++;

            previous = match.Index;

            var key = Regex.Unescape(match.Groups["key"].Value);
            if (key.Length == 0)
                continue;

            if (seen.TryGetValue(key, out var firstLine))
                return $"'{key}' on line {line} (first declared on line {firstLine})";

            seen[key] = line;
        }

        return null;
    }

    private static string KeysTypeScript(string fileName, List<string> keys)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine($"// Generated from {fileName}; edit the translation file, not this file.");
        builder.AppendLine("import type {} from \"@photino-net-utils/bridge\";");
        builder.AppendLine();
        builder.AppendLine("declare module \"@photino-net-utils/bridge\"");
        builder.AppendLine("{");
        builder.AppendLine("    interface LocalizationKeys");
        builder.AppendLine("    {");

        foreach (var key in keys)
            builder.AppendLine($"        {Quote(key)}: true;");

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string TsType(string type)
    {
        var nullable = type.EndsWith('?');
        var value = type.TrimEnd('?').Trim();
        var mapped = MapType(value);
        return nullable ? $"{mapped} | null" : mapped;
    }

    private static string MapType(string type)
    {
        type = Clr(type);

        switch (type)
        {
            case "string":
            case "char":
            case "DateTime":
            case "DateTimeOffset":
            case "Guid":
            case "TimeSpan":
                return "string";
            case "bool":
                return "boolean";
            case "byte":
            case "sbyte":
            case "short":
            case "ushort":
            case "int":
            case "uint":
            case "long":
            case "ulong":
            case "float":
            case "double":
            case "decimal":
                return "number";
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
            return $"{MapType(type[..^2].Trim())}[]";

        var generic = GenericPattern().Match(type);
        if (generic.Success)
        {
            var outer = generic.Groups["outer"].Value;
            var inner = generic.Groups["inner"].Value;

            if (outer is "List" or "IList" or "IReadOnlyList" or "IEnumerable" or "ICollection"
                or "IReadOnlyCollection" or "HashSet" or "ISet" or "IReadOnlySet")
                return $"{MapType(inner)}[]";

            if (outer is "Dictionary" or "IDictionary" or "IReadOnlyDictionary")
            {
                var parts = SplitTopLevel(inner);
                if (parts.Count == 2)
                    return $"Record<string, {MapType(parts[1])}>";
            }
        }

        return "unknown";
    }

    private static Dictionary<string, object?> JsonType(string type)
    {
        var nullable = type.EndsWith('?');
        var value = type.TrimEnd('?').Trim();
        var schema = JsonTypeCore(value);
        return nullable ? Nullable(schema) : schema;
    }

    private static Dictionary<string, object?> JsonTypeCore(string type)
    {
        type = Clr(type);

        switch (type)
        {
            case "string":
            case "char":
            case "DateTime":
            case "DateTimeOffset":
            case "Guid":
            case "TimeSpan":
                return new()
                {
                    ["type"] = "string"
                };
            case "bool":
                return new()
                {
                    ["type"] = "boolean"
                };
            case "byte":
            case "sbyte":
            case "short":
            case "ushort":
            case "int":
            case "uint":
            case "long":
            case "ulong":
                return new()
                {
                    ["type"] = "integer"
                };
            case "float":
            case "double":
            case "decimal":
                return new()
                {
                    ["type"] = "number"
                };
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
            return new()
            {
                ["type"] = "array",
                ["items"] = JsonType(type[..^2].Trim())
            };

        var generic = GenericPattern().Match(type);
        if (generic.Success)
        {
            var outer = generic.Groups["outer"].Value;
            var inner = generic.Groups["inner"].Value;

            if (outer is "List" or "IList" or "IReadOnlyList" or "IEnumerable" or "ICollection"
                or "IReadOnlyCollection" or "HashSet" or "ISet" or "IReadOnlySet")
                return new()
                {
                    ["type"] = "array",
                    ["items"] = JsonType(inner)
                };

            if (outer is "Dictionary" or "IDictionary" or "IReadOnlyDictionary")
            {
                var parts = SplitTopLevel(inner);
                if (parts.Count == 2)
                    return new()
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = JsonType(parts[1])
                    };
            }
        }

        return new();
    }

    private static Dictionary<string, object?> Nullable(Dictionary<string, object?> schema)
    {
        if (schema.TryGetValue("type", out var type) && type is string value)
        {
            schema["type"] = new[]
            {
                value, "null"
            };
            return schema;
        }

        return new()
        {
            ["anyOf"] = new object[]
            {
                schema, new Dictionary<string, object?>
                {
                    ["type"] = "null"
                }
            }
        };
    }

    /// <summary>
    ///     Removes a namespace and turns a CLR name into its keyword, so <c>System.Int32</c> and
    ///     <c>global::System.Collections.Generic.List&lt;string&gt;</c> map like <c>int</c> and
    ///     <c>List&lt;string&gt;</c>.
    /// </summary>
    private static string Clr(string type)
    {
        var value = type.Replace("global::", "").Trim();
        var generic = value.IndexOf('<');
        var head = generic < 0 ? value : value[..generic];
        var dot = head.LastIndexOf('.');

        if (dot >= 0)
            value = value[(dot + 1)..];

        var open = value.IndexOf('<');
        var name = open < 0 ? value : value[..open];

        return ClrNames.TryGetValue(name, out var alias) ? alias + value[name.Length..] : value;
    }

    private static List<string> SplitTopLevel(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < value.Length; index++)
            switch (value[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }

        parts.Add(value[start..].Trim());
        return parts;
    }

    [GeneratedRegex(@"^(?<outer>[\w.]+)\s*<\s*(?<inner>[\s\S]+)\s*>$")]
    private static partial Regex GenericPattern();

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[ \t]*\"(?<key>(?:\\\\.|[^\"\\\\])*)\"[ \t]*:", RegexOptions.Multiline)]
    private static partial Regex KeyLinePattern();

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is { Length: > 0 })
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, content);
    }

    private sealed record Options(
        string? Assembly,
        string? Output,
        string? Schema,
        string? I18n,
        string? I18nOutput);
}