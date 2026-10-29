using System.Globalization;
using System.Reflection;
using System.Text.Json;
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
///     A locale is merged over its language and then over the default locale, so a partially
///     translated file still shows something sensible. Call <see cref="Initialize" /> once, before
///     anything asks for a string.
/// </remarks>
[PublicAPI]
public static class Localization
{
    public const string DefaultLocale = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Sync = new();

    private static Assembly? _assembly;
    private static string _resourcePrefix = "";
    private static string _defaultLocale = DefaultLocale;
    private static Func<string>? _currentLocale;
    private static bool _listening;

    /// <summary>Locales discovered from the embedded translation files.</summary>
    public static IReadOnlyList<string> AvailableLocales { get; private set; } = [];

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

        if (_listening)
            return;

        _listening = true;
        NativeBridge.MessageReceived += HandleMessageAsync;
    }

    /// <summary>The language the app is using right now.</summary>
    public static string CurrentLanguage =>
        _currentLocale?.Invoke() is { Length: > 0 } locale ? locale : _defaultLocale;

    /// <summary>Looks a key up in the current language.</summary>
    public static string Get(string key) => Get(CurrentLanguage, key);

    /// <summary>Looks a key up in <paramref name="locale" />, falling back to the default locale.</summary>
    public static string Get(string locale, string key) =>
        Load(locale).TryGetValue(key, out var value) ? value : key;

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
        var locale = string.IsNullOrWhiteSpace(requested) ? CurrentLanguage : requested;

        return Task.FromResult<NativeResponse?>(
            context.Reply("i18n.get.result", new LocalePayload(locale, Load(locale), AvailableLocales)));
    }

    private static Dictionary<string, string> Load(string locale)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(locale, out var cached))
                return cached;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            return new();

        return Try.Run(() => JsonSerializer.Deserialize(stream, NativeJson.TypeInfo<Dictionary<string, string>>())
                             ?? new Dictionary<string, string>())
            .GetOrElse(ex =>
            {
                Log.Warning(ex, "Failed to read the translation file for {Locale}", locale);
                return new();
            });
    }

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
