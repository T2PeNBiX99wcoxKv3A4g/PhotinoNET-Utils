using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Localization;

/// <summary>Body for <c>i18n.get</c>: the requested locale, or null for the one in use.</summary>
[PublicAPI]
public sealed record LocaleRequest(string? Locale);

/// <summary>Body for <c>i18n.get.result</c>: the merged strings of one locale and every locale available.</summary>
[PublicAPI]
public sealed record LocalePayload(string Locale, Dictionary<string, string> Strings, IReadOnlyList<string> Available);

/// <summary>
///     Loads UI strings from embedded <c>{resourcePrefix}{locale}.jsonc</c> resources, so the desktop
///     backend and the React front-end read the exact same text.
/// </summary>
/// <remarks>
///     <para>
///         A locale is merged over its language and then over the default locale, so a partially
///         translated file still shows something sensible. Call <see cref="Initialize" /> once, before
///         anything asks for a string.
///     </para>
///     <para>
///         Keys are case-insensitive: <c>Localization.Get("TRAY.OPEN")</c> returns the text of
///         <c>"tray.open"</c>. That also means one file must not declare the same key twice with a
///         different spelling; the build reports that as <c>PNU2102</c> and keeps the first entry at
///         run time. Fix a duplicate by renaming or removing the key in the <c>.jsonc</c> file, never
///         by removing the build check.
///     </para>
///     <para>
///         A key is a lowercase path such as <c>ui.section.open_key</c>. A file that breaks that shape
///         stops <see cref="Initialize" />, so an app that built with the analyzer disabled still
///         cannot ship a sentence as a key.
///     </para>
/// </remarks>
[PublicAPI]
public static partial class Localization
{
    public const string DefaultLocale = "en";

    /// <summary>Stored setting that asks the library to follow the OS UI culture.</summary>
    public const string SystemLocale = "system";

    /// <summary>
    ///     How keys are compared everywhere: the backend, the generated constants and the UI. Keeping
    ///     one comparer is what makes <c>Localization.Get("TRAY.OPEN")</c> work.
    /// </summary>
    public static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(KeyComparer);
    private static readonly Lock Sync = new();

    private static Assembly? _assembly;
    private static string _resourcePrefix = "";
    private static string _defaultLocale = DefaultLocale;
    private static Func<string>? _currentLocale;
    private static bool _listening;

    /// <summary>Locales discovered from the embedded translation files.</summary>
    public static IReadOnlyList<string> AvailableLocales { get; private set; } = [];

    /// <summary>The language the app is using right now.</summary>
    public static string CurrentLanguage => Resolve(_currentLocale?.Invoke());

    /// <summary>Maps the stored setting to a concrete locale; <c>system</c> follows the OS.</summary>
    public static string Resolve(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return _defaultLocale;

        return locale.Equals(SystemLocale, StringComparison.OrdinalIgnoreCase)
            ? ResolveSystemLocale()
            : locale;
    }

    /// <summary>
    ///     Turns a stored language value into one of <see cref="AvailableLocales" />. <c>system</c>
    ///     stays as it is, a matching locale is returned in its canonical spelling, and anything the
    ///     app does not ship falls back to the default locale.
    /// </summary>
    public static string Normalize(string? locale, string? defaultLocale = null)
    {
        if (string.IsNullOrWhiteSpace(locale) || locale.Equals(SystemLocale, StringComparison.OrdinalIgnoreCase))
            return SystemLocale;

        var available = AvailableLocales;
        var exact = available.FirstOrDefault(item => item.Equals(locale, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // "ja-JP" should select ja when only ja.jsonc exists, and "zh-tw" should select zh-TW.
        var language = locale.Split('-', '_')[0];
        return available.FirstOrDefault(item =>
                   item.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase))
               ?? defaultLocale
               ?? _defaultLocale;
    }

    /// <summary>
    ///     Starts answering <c>i18n.get</c> from <paramref name="assembly" />'s resources.
    ///     <paramref name="currentLocale" /> supplies the language the settings selected.
    /// </summary>
    public static void Initialize(Assembly assembly, string resourcePrefix, Func<string> currentLocale,
        string defaultLocale = DefaultLocale)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix;
        _currentLocale = currentLocale;
        _defaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? DefaultLocale : defaultLocale;
        AvailableLocales = DiscoverLocales();

        // Validate every shipped file now, so a key that skipped the analyzer stops the app at
        // startup instead of quietly showing the key itself.
        foreach (var locale in AvailableLocales)
            _ = ReadFile(locale);

        if (_listening)
            return;

        _listening = true;
        NativeBridge.MessageReceived += HandleMessageAsync;
    }

    /// <summary>Looks a key up in the current language.</summary>
    public static string Get(string key) => Get(CurrentLanguage, key);

    /// <summary>Looks a key up in <paramref name="locale" />, falling back to the default locale.</summary>
    public static string Get(string locale, string key) =>
        Load(Resolve(locale)).TryGetValue(key, out var value) ? value : key;

    /// <summary>Best matching embedded locale for the OS UI culture.</summary>
    public static string ResolveSystemLocale()
    {
        var culture = CultureInfo.CurrentUICulture.Name;

        return AvailableLocales.FirstOrDefault(locale => locale.Equals(culture, StringComparison.OrdinalIgnoreCase))
               ?? AvailableLocales.FirstOrDefault(locale =>
                   locale.Split('-')[0].Equals(culture.Split('-')[0], StringComparison.OrdinalIgnoreCase))
               ?? _defaultLocale;
    }

    private static Task<NativeResponse?> HandleMessageAsync(NativeContext context)
    {
        if (context.Request.Type != "i18n.get")
            return Task.FromResult<NativeResponse?>(null);

        var requested = context.Payload<LocaleRequest>()?.Locale;
        var locale = string.IsNullOrWhiteSpace(requested) ? CurrentLanguage : Resolve(requested);

        return Task.FromResult<NativeResponse?>(
            context.Reply("i18n.get.result", new LocalePayload(locale, Load(locale), AvailableLocales)));
    }

    private static Dictionary<string, string> Load(string locale)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(locale, out var cached))
                return cached;

            var merged = new Dictionary<string, string>(KeyComparer);
            foreach (var candidate in FallbackChain(locale))
            foreach (var pair in ReadFile(candidate))
                merged.TryAdd(pair.Key, pair.Value);

            Cache[locale] = merged;
            return merged;
        }
    }

    private static Dictionary<string, string> ReadFile(string locale)
    {
        using var stream = _assembly?.GetManifestResourceStream($"{_resourcePrefix}{locale}.jsonc");
        if (stream is null)
            return new(KeyComparer);

        var table = new Dictionary<string, string>(KeyComparer);

        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            foreach (var property in document.RootElement.EnumerateObject())
            {
                // The analyzer and the TypeScript generator already refuse this file; checking here
                // too means a build that disabled them still cannot show a sentence as a key.
                if (!KeyShape().IsMatch(property.Name))
                    throw new InvalidOperationException(
                        $"Invalid localization key '{property.Name}' in {locale}.jsonc; use lowercase " +
                        "letters, digits, '_' and '.', for example ui.section.open_key.");

                // Re-key with the shared comparer so lookups ignore case. A file with two spellings
                // of one key is a build error (PNU2102); keeping the first entry only prevents a
                // crash in a build that skipped the analyzer.
                table.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.GetRawText());
            }
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to read the translation file for {Locale}", locale);
            return new(KeyComparer);
        }

        return table;
    }

    /// <summary>The only accepted key shape; PNU2103 and the TypeScript generator share it.</summary>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)*$")]
    private static partial Regex KeyShape();

    private static IEnumerable<string> FallbackChain(string locale)
    {
        yield return locale;

        var language = locale.Split('-')[0];
        if (!language.Equals(locale, StringComparison.OrdinalIgnoreCase))
            yield return language;

        if (!_defaultLocale.Equals(locale, StringComparison.OrdinalIgnoreCase))
            yield return _defaultLocale;
    }

    private static IReadOnlyList<string> DiscoverLocales()
    {
        const string suffix = ".jsonc";

        return
        [
            .. (_assembly?.GetManifestResourceNames() ?? [])
            .Where(name => name.StartsWith(_resourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(suffix, StringComparison.Ordinal))
            .Select(name => name[_resourcePrefix.Length..^suffix.Length])
            .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }
}