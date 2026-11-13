using System.Globalization;
using System.Text;

namespace PhotinoNetUtils.CodeGen;

/// <summary>
///     Reads the <c>[SettingsModel]</c> type and its <c>[Setting]</c> members out of a C# file.
/// </summary>
/// <remarks>
///     A scanner rather than a set of regular expressions. A regular expression has to guess where
///     an attribute ends, so an attribute written over several lines, with its namespace spelled
///     out, with the positional comment, or with a <c>]</c> inside its text used to disappear from
///     the generated interface. Here only the C# rules for comments, strings and brackets matter,
///     so every spelling of the same attribute reads the same.
/// </remarks>
internal static class SettingsSource
{
    private static readonly HashSet<string> Modifiers =
    [
        with(StringComparer.Ordinal),
        "public", "private", "protected", "internal", "static", "readonly", "required", "new",
        "virtual", "override", "abstract", "sealed", "unsafe", "extern", "partial", "const", "file"
    ];

    private static readonly HashSet<string> Targets =
    [
        with(StringComparer.Ordinal),
        "assembly", "event", "field", "method", "module", "param", "property", "return", "type"
    ];

    public static SettingsFile Read(string text)
    {
        var properties = new List<SettingProperty>();
        var errors = new List<string>();
        var warnings = new List<string>();
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var constants = Constants(text);
        string? model = null;

        for (var i = 0; i < text.Length;)
        {
            var token = SkipToken(text, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            // Any `[` may begin an attribute list. A collection expression or an indexer also opens
            // one, but its contents never name a Setting, so it costs a scan and nothing else.
            if (text[i] != '[')
            {
                i++;
                continue;
            }

            var listEnd = SkipBalanced(text, i, '[', ']');
            var attributes = Attributes(text[(i + 1)..(listEnd - 1)]);
            var declarations = Declarations(text, listEnd);
            var line = Line(text, i);

            var setting = attributes.FirstOrDefault(item => IsAttribute(item.Name, "Setting"));

            if (setting is not null)
            {
                if (ReadMember(text, declarations) is not { } member)
                {
                    warnings.Add($"[Setting] on line {line} is not on a property or a record parameter; skipped.");
                }
                else
                {
                    var args = Arguments(setting, line, warnings, constants);
                    var key = args.Key ?? CamelCase(member.Name);

                    if (keys.TryGetValue(key, out var first))
                    {
                        errors.Add($"duplicate setting key '{key}' on line {line} and on line {first}; " +
                                   "rename one of them.");
                    }
                    else
                    {
                        keys[key] = line;
                        properties.Add(new(key, member.Type, args.Comment, args.Min, args.Max));
                    }
                }
            }
            else if (attributes.Any(item => IsAttribute(item.Name, "SettingsModel")))
            {
                if (ReadType(text, declarations) is { } type)
                {
                    if (model is not null && model != type.Name)
                        errors.Add($"the file declares more than one settings model ('{model}' and '{type.Name}'); " +
                                   "point --model at one model file.");

                    model ??= type.Name;

                    if (type.Partial)
                        warnings.Add($"'{type.Name}' is partial; settings declared in another file are not read.");
                }
            }

            i = listEnd;
        }

        if (text.Contains("#if", StringComparison.Ordinal))
            warnings.Add("the file uses #if; every branch is read, so a setting from an inactive branch may be generated.");

        return new(model, properties, errors, warnings);
    }

    private static AttributeArguments Arguments(
        AttributeInfo attribute,
        int line,
        List<string> warnings,
        IReadOnlyDictionary<string, string> constants)
    {
        string? comment = null;
        string? key = null;
        double? min = null;
        double? max = null;
        var positional = 0;

        foreach (var argument in Split(attribute.Arguments ?? ""))
        {
            var assignment = Assignment(argument);

            // A positional argument is the comment; the constructor takes it so an attribute can be
            // written without naming anything.
            if (assignment < 0)
            {
                if (positional++ == 0)
                    comment = Value(argument, constants) as string;

                continue;
            }

            var name = argument[..assignment].Trim();
            var value = argument[(assignment + 1)..].Trim();

            switch (name)
            {
                case "Comment":
                    comment = Value(value, constants) as string;
                    if (comment is null)
                        warnings.Add($"[Setting] on line {line} has a comment the generator cannot read; " +
                                     "only a literal string or a const is used.");
                    break;
                case "Key":
                    key = Value(value, constants) as string;
                    if (key is null)
                        warnings.Add($"[Setting] on line {line} has a key the generator cannot read; " +
                                     "the member name is used instead.");
                    break;
                case "Min":
                case "Max":
                    var number = Value(value, constants) as double?;
                    if (name == "Min") min = number; else max = number;
                    if (number is null)
                        warnings.Add($"[Setting] on line {line} has a {name.ToLowerInvariant()} the generator " +
                                     "cannot read; only a number literal or a const is used.");
                    break;
            }
        }

        return new(comment, key, min, max);
    }

    /// <summary>
    ///     Evaluates an attribute argument the way the compiler would for the common cases: a
    ///     literal, <c>nameof(...)</c>, concatenated literals, or a <c>const</c> declared in the
    ///     file. Anything else returns null, which the caller reports instead of guessing.
    /// </summary>
    private static object? Value(string expression, IReadOnlyDictionary<string, string> constants, int depth = 0)
    {
        var text = expression.Trim();
        if (text.Length == 0 || depth > 8)
            return null;

        if (text[0] == '(' && SkipBalanced(text, 0, '(', ')') == text.Length)
            return Value(text[1..^1], constants, depth + 1);

        if (WordAt(text, 0, "nameof"))
        {
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');

            if (open > 0 && close > open)
            {
                var inside = text[(open + 1)..close].Trim();
                var start = NameStart(inside);

                if (start >= 0 && start < inside.Length)
                    return inside[start..].Trim().TrimStart('@');
            }

            return null;
        }

        var parts = Split(text, '+');

        if (parts.Count > 1)
        {
            var combined = "";

            foreach (var part in parts)
            {
                if (Value(part, constants, depth + 1) is not string piece)
                    return null;

                combined += piece;
            }

            return combined;
        }

        if (StringValue(text) is { } literal)
            return literal;

        if (NumberValue(text) is { } number)
            return number;

        if (text.All(character => char.IsLetterOrDigit(character) || character is '_' or '.' or '@'))
        {
            var name = text[(text.LastIndexOf('.') + 1)..].TrimStart('@');

            return constants.TryGetValue(name, out var declared) ? Value(declared, constants, depth + 1) : null;
        }

        return null;
    }

    /// <summary>Skips whitespace, comments and string literals, so the scanner never sees their text.</summary>
    private static int SkipToken(string text, int i)
    {
        if (char.IsWhiteSpace(text[i]) || IsCommentStart(text, i))
            return SkipTrivia(text, i);

        var literal = SkipString(text, i);
        return literal > i ? literal : i;
    }

    private static int SkipTrivia(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (!IsCommentStart(text, i))
                return i;

            i = SkipComment(text, i);
        }

        return i;
    }

    private static bool IsCommentStart(string text, int i) =>
        text[i] == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*';

    private static int SkipComment(string text, int i)
    {
        if (text[i + 1] == '/')
        {
            var end = text.IndexOf('\n', i);
            return end < 0 ? text.Length : end + 1;
        }

        var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return close < 0 ? text.Length : close + 2;
    }

    /// <summary>Skips a string or character literal, including the verbatim, raw and interpolated forms.</summary>
    private static int SkipString(string text, int i)
    {
        if (text[i] == '\'')
        {
            for (var j = i + 1; j < text.Length; j++)
            {
                if (text[j] == '\\')
                {
                    j++;
                    continue;
                }

                if (text[j] == '\'')
                    return j + 1;
            }

            return text.Length;
        }

        var start = i;
        var verbatim = false;
        var interpolated = false;

        while (i < text.Length && text[i] is '$' or '@')
        {
            if (text[i] == '$') interpolated = true; else verbatim = true;
            i++;
        }

        if (i >= text.Length || text[i] != '"')
            return start;

        var quotes = 0;
        while (i + quotes < text.Length && text[i + quotes] == '"')
            quotes++;

        if (quotes >= 3)
        {
            var closing = text.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
            return closing < 0 ? text.Length : closing + quotes;
        }

        for (i++; i < text.Length;)
        {
            var c = text[i];

            if (c == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            if (c == '\\' && !verbatim)
            {
                i += 2;
                continue;
            }

            if (c == '{' && interpolated)
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }

                i = SkipBalanced(text, i, '{', '}');
                continue;
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>Returns the index after the bracket that closes the one at <paramref name="i" />.</summary>
    private static int SkipBalanced(string text, int i, char open, char close)
    {
        var depth = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (IsCommentStart(text, i))
            {
                i = SkipComment(text, i);
                continue;
            }

            var literal = SkipString(text, i);
            if (literal > i)
            {
                i = literal;
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                return i + 1;
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>Position after every attribute list stacked on the declaration that follows.</summary>
    private static int Declarations(string text, int i)
    {
        while (true)
        {
            var next = SkipTrivia(text, i);
            if (next >= text.Length || text[next] != '[')
                return next;

            i = SkipBalanced(text, next, '[', ']');
        }
    }

    private static List<AttributeInfo> Attributes(string body)
    {
        var attributes = new List<AttributeInfo>();

        foreach (var part in Split(body))
        {
            var text = Target(part);
            if (text.Length == 0)
                continue;

            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            var name = (open < 0 ? text : text[..open]).Trim();
            var arguments = open < 0 || close <= open ? null : text[(open + 1)..close];

            if (name.Length > 0)
                attributes.Add(new(name, arguments));
        }

        return attributes;
    }

    /// <summary>Strips an attribute target such as <c>property:</c>; a <c>global::</c> alias stays.</summary>
    private static string Target(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon + 1 < text.Length && text[colon + 1] == ':' || text[colon - 1] == ':')
            return text.Trim();

        return Targets.Contains(text[..colon].Trim()) ? text[(colon + 1)..].Trim() : text.Trim();
    }

    private static bool IsAttribute(string name, string expected)
    {
        var simple = name[(Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':')) + 1)..];
        return simple == expected || simple == expected + "Attribute";
    }

    /// <summary>Splits on a separator that is not inside a string, a comment or a bracket.</summary>
    private static List<string> Split(string text, char separator = ',')
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length;)
        {
            var token = SkipToken(text, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            switch (text[i])
            {
                case '(' or '[' or '{' or '<':
                    depth++;
                    break;
                case ')' or ']' or '}' or '>':
                    if (depth > 0) depth--;
                    break;
                default:
                    if (text[i] == separator && depth == 0)
                    {
                        parts.Add(text[start..i]);
                        start = i + 1;
                    }

                    break;
            }

            i++;
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Index of the <c>=</c> that assigns a named attribute argument, or -1.</summary>
    private static int Assignment(string argument)
    {
        var depth = 0;

        for (var i = 0; i < argument.Length;)
        {
            var token = SkipToken(argument, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            var c = argument[i];

            switch (c)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    if (depth > 0) depth--;
                    break;
                case '=' when depth == 0:
                    var previous = i > 0 ? argument[i - 1] : '\0';
                    var next = i + 1 < argument.Length ? argument[i + 1] : '\0';
                    if (previous is not ('=' or '!' or '<' or '>') && next is not ('=' or '>'))
                        return i;
                    break;
            }

            i++;
        }

        return -1;
    }

    private static Member? ReadMember(string text, int start)
    {
        var head = Head(text, start, out var terminator, out var end);
        if (head is null)
            return null;

        var nameStart = NameStart(head);
        if (nameStart <= 0 || nameStart >= head.Length)
            return null;

        var type = TypeName(head[..nameStart]);
        if (type.Length == 0)
            return null;

        var name = head[nameStart..].Trim().TrimStart('@');
        var member = Modifiers.Contains(FirstWord(head));

        // Only an instance property the serializer writes is a setting; a static or non-public
        // member never reaches the payload. A record parameter has no modifier and no getter.
        if (member && (!HasModifier(head, "public") || HasModifier(head, "static") || HasModifier(head, "const")))
            return null;

        return terminator switch
        {
            '{' when HasGetter(text, end) => new(name, type),
            '=' or ',' or ')' when !member => new(name, type),
            _ => null
        };
    }

    /// <summary>Every <c>const</c> declared in the file, by simple name, with its value text.</summary>
    private static Dictionary<string, string> Constants(string text)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < text.Length;)
        {
            var token = SkipToken(text, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            if (!WordAt(text, i, "const"))
            {
                i++;
                continue;
            }

            var head = Head(text, i, out var terminator, out var end);
            var nameStart = head is null ? -1 : NameStart(head);

            if (head is null || terminator != '=' || nameStart <= 0 || nameStart >= head.Length)
            {
                i++;
                continue;
            }

            var name = head[nameStart..].Trim().TrimStart('@');
            var close = StatementEnd(text, end + 1);
            constants[name] = text[(end + 1)..close];
            i = close + 1;
        }

        return constants;
    }

    /// <summary>Index of the <c>;</c> that ends the statement at <paramref name="i" />.</summary>
    private static int StatementEnd(string text, int i)
    {
        var depth = 0;

        while (i < text.Length)
        {
            var token = SkipToken(text, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            if (text[i] is '(' or '[' or '{')
            {
                depth++;
            }
            else if (text[i] is ')' or ']' or '}')
            {
                if (depth > 0) depth--;
            }
            else if (text[i] == ';' && depth == 0)
            {
                return i;
            }

            i++;
        }

        return text.Length;
    }

    private static bool WordAt(string text, int i, string word) =>
        string.CompareOrdinal(text, i, word, 0, word.Length) == 0 &&
        (i + word.Length >= text.Length || !IsWord(text[i + word.Length])) &&
        (i == 0 || !IsWord(text[i - 1]));

    private static ModelType? ReadType(string text, int start)
    {
        var head = Head(text, start, out var terminator, out _, stopAtParenthesis: true);
        if (head is null)
            return null;

        // A generic model lists its constraints after `where`; the name is before them.
        var where = head.IndexOf(" where ", StringComparison.Ordinal);
        if (where > 0)
            head = head[..where];

        var nameStart = NameStart(head);
        if (nameStart <= 0 || nameStart >= head.Length || terminator is not ('{' or ';' or ':' or '('))
            return null;

        return new(head[nameStart..].Trim().TrimStart('@'), HasModifier(head, "partial"));
    }

    /// <summary>Reads a declaration up to the bracket that ends its head.</summary>
    private static string? Head(string text, int start, out char terminator, out int end,
        bool stopAtParenthesis = false)
    {
        var i = SkipTrivia(text, start);
        var headStart = i;
        var angle = 0;
        var paren = 0;
        var bracket = 0;

        while (i < text.Length)
        {
            var token = SkipToken(text, i);
            if (token > i)
            {
                i = token;
                continue;
            }

            var c = text[i];
            end = i;
            terminator = c;

            if (c == '<')
            {
                angle++;
            }
            else if (c == '>')
            {
                if (angle > 0) angle--;
            }
            else if (c == '[')
            {
                bracket++;
            }
            else if (c == ']')
            {
                if (bracket > 0) bracket--;
            }
            else if (c == '(')
            {
                if (stopAtParenthesis && angle == 0 && bracket == 0)
                    return text[headStart..i];

                paren++;
            }
            else if (c == ')')
            {
                if (paren > 0) paren--;
                else if (angle == 0 && bracket == 0) return text[headStart..i];
            }
            else if (angle == 0 && paren == 0 && bracket == 0 && c is '{' or '=' or ',' or ';')
            {
                return text[headStart..i];
            }
            else if (c == ':' && angle == 0 && paren == 0 && bracket == 0 && !IsAliasColon(text, i))
            {
                return text[headStart..i];
            }

            i++;
        }

        end = text.Length;
        terminator = '\0';
        return null;
    }

    /// <summary>True when a property body declares a <c>get</c> accessor.</summary>
    private static bool HasGetter(string text, int brace)
    {
        var end = SkipBalanced(text, brace, '{', '}');
        var body = text[brace..end];

        for (var i = body.IndexOf("get", StringComparison.Ordinal); i >= 0;
             i = body.IndexOf("get", i + 3, StringComparison.Ordinal))
            if ((i == 0 || !IsWord(body[i - 1])) && (i + 3 >= body.Length || !IsWord(body[i + 3])))
                return true;

        return false;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>True for the colons of an alias such as <c>global::System.String</c>.</summary>
    private static bool IsAliasColon(string text, int i) =>
        i + 1 < text.Length && text[i + 1] == ':' || i > 0 && text[i - 1] == ':';

    private static int NameStart(string head)
    {
        var end = head.Length;

        while (end > 0 && char.IsWhiteSpace(head[end - 1]))
            end--;

        while (end > 0 && IsWord(head[end - 1]))
            end--;

        return end > 0 && head[end - 1] == '@' ? end - 1 : end;
    }

    private static string TypeName(string head)
    {
        var text = head.Trim();

        while (true)
        {
            var space = text.IndexOfAny([' ', '\t', '\n', '\r']);
            if (space <= 0 || !Modifiers.Contains(text[..space]))
                return text;

            text = text[(space + 1)..].TrimStart();
        }
    }

    private static string FirstWord(string head)
    {
        var space = head.IndexOfAny([' ', '\t', '\n', '\r']);
        return space < 0 ? head : head[..space];
    }

    private static bool HasModifier(string head, string modifier)
    {
        var text = head.Trim();

        while (text.Length > 0)
        {
            var space = text.IndexOfAny([' ', '\t', '\n', '\r']);
            var word = space < 0 ? text : text[..space];

            if (word == modifier)
                return true;

            if (!Modifiers.Contains(word) || space < 0)
                return false;

            text = text[(space + 1)..].TrimStart();
        }

        return false;
    }

    private static string? StringValue(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return null;

        var i = 0;
        var verbatim = false;

        while (i < value.Length && value[i] is '$' or '@')
        {
            verbatim |= value[i] == '@';
            i++;
        }

        if (i >= value.Length || value[i] != '"')
            return null;

        var quotes = 0;
        while (i + quotes < value.Length && value[i + quotes] == '"')
            quotes++;

        var start = i + quotes;

        if (quotes >= 3)
        {
            var end = value.IndexOf(new string('"', quotes), start, StringComparison.Ordinal);
            return end < 0 ? null : Raw(value[start..end]);
        }

        var close = start;
        while (close < value.Length)
        {
            if (value[close] == '\\' && !verbatim)
            {
                close += 2;
                continue;
            }

            if (value[close] != '"')
            {
                close++;
                continue;
            }

            if (verbatim && close + 1 < value.Length && value[close + 1] == '"')
            {
                close += 2;
                continue;
            }

            break;
        }

        if (close >= value.Length)
            return null;

        var content = value[start..close];
        return verbatim ? content.Replace("\"\"", "\"") : Unescape(content);
    }

    /// <summary>Applies the raw string rules: the closing quotes decide how much indentation to drop.</summary>
    private static string Raw(string content)
    {
        content = content.Replace("\r\n", "\n");

        if (!content.Contains('\n'))
            return content;

        var lines = content.Split('\n').ToList();

        if (lines[0].Trim().Length == 0)
            lines.RemoveAt(0);

        if (lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var indent = lines.Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line)).TrimEnd();
    }

    private static string Unescape(string content)
    {
        var builder = new StringBuilder(content.Length);

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (c != '\\' || i + 1 >= content.Length)
            {
                builder.Append(c);
                continue;
            }

            var next = content[++i];

            switch (next)
            {
                case '0':
                    builder.Append('\0');
                    break;
                case 'a':
                    builder.Append('\a');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'v':
                    builder.Append('\v');
                    break;
                case 'u' when i + 4 < content.Length:
                    builder.Append((char)Hex(content[(i + 1)..(i + 5)]));
                    i += 4;
                    break;
                case 'U' when i + 8 < content.Length:
                {
                    var code = Hex(content[(i + 1)..(i + 9)]);
                    builder.Append(code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
                        ? char.ConvertFromUtf32(code)
                        : "\uFFFD");
                    i += 8;
                    break;
                }
                default:
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }

    private static int Hex(string text) =>
        int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0xFFFD;

    private static double? NumberValue(string value)
    {
        var text = value.Trim().Replace("_", "");

        if (text.Length > 0 && text[^1] is 'd' or 'D' or 'f' or 'F' or 'm' or 'M')
            text = text[..^1];

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static int Line(string text, int index)
    {
        var line = 1;

        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n')
                line++;

        return line;
    }

    private static string CamelCase(string name) =>
        char.ToLowerInvariant(name[0]) + name[1..];

    private sealed record AttributeInfo(string Name, string? Arguments);

    private sealed record Member(string Name, string Type);

    private sealed record ModelType(string Name, bool Partial);
}

/// <summary>The values one <c>[Setting]</c> carries.</summary>
internal sealed record AttributeArguments(string? Comment, string? Key, double? Min, double? Max);

/// <summary>One property as the generated interface and schema see it.</summary>
internal sealed record SettingProperty(string Key, string Type, string? Comment, double? Min, double? Max);

/// <summary>Everything the generator found in one settings file.</summary>
internal sealed record SettingsFile(
    string? ModelName,
    List<SettingProperty> Properties,
    List<string> Errors,
    List<string> Warnings);
