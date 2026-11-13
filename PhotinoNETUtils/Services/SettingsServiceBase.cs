using JetBrains.Annotations;
using PhotinoNetUtils.Hosting;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Settings;
using Serilog;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Owns one settings model and answers the standard <c>settings.get</c> / <c>settings.save</c>
///     messages, so an app only supplies the payload, its validation and its side effects.
/// </summary>
/// <remarks>
///     <typeparamref name="TSelf" /> is the concrete service, which lets the base expose
///     <see cref="Instance" />; <typeparamref name="T" /> is the model read from the store;
///     <typeparamref name="TPayload" /> is the shape the interface reads. Register the payload in the
///     app's serializer context, because the bridge serializes it through that context.
/// </remarks>
[PublicAPI]
public abstract class SettingsServiceBase<TSelf, T, TPayload> : IDisposable
    where TSelf : SettingsServiceBase<TSelf, T, TPayload>, new()
    where T : class
    where TPayload : class
{
    private static TSelf? _instance;
    protected readonly ILogger Log = Serilog.Log.ForContext<T>();

    private readonly Action<string, object?> _notify;
    private readonly ISettingsStore<T> _store;

    /// <param name="store">The file the settings live in.</param>
    /// <param name="notify">
    ///     Pushes the <c>settings.changed</c> event. Defaults to <see cref="WebViewEvents" />, which is
    ///     right for a single-window app; a multi-window app passes its own broadcaster.
    /// </param>
    protected SettingsServiceBase(ISettingsStore<T> store, Action<string, object?>? notify = null)
    {
        if (Interlocked.Exchange(ref _instance, (TSelf)this) is not null)
            throw new InvalidOperationException(
                $"{typeof(TSelf).Name} is a settings singleton; use {typeof(TSelf).Name}.Instance.");

        _store = store;
        _notify = notify ?? WebViewEvents.Notify;
        Current = store.Current;
        NativeBridge.MessageReceived += HandleMessageAsync;
    }

    /// <summary>The process-wide service, created on first use.</summary>
    public static TSelf Instance { get; } = new();

    /// <summary>The settings file this service reads and writes.</summary>
    public string FilePath => _store.FilePath;

    /// <summary>The settings in use; updated by <see cref="LoadAsync" /> and <see cref="SaveAsync" />.</summary>
    public T Current { get; private set; }

    public void Dispose() => NativeBridge.MessageReceived -= HandleMessageAsync;

    /// <summary>Raised after a save, for backend services that follow the settings.</summary>
    public event Action<T>? Changed;

    /// <summary>Reads the store and normalizes the result.</summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        Current = NormalizeAll(await _store.LoadAsync(token));
        OnLoaded(Current);
    }

    /// <summary>Normalizes, stores and publishes the settings. Returns false when the write failed.</summary>
    public Task<bool> SaveAsync(T settings, CancellationToken token = default) =>
        SaveCoreAsync(settings, true, token);

    /// <summary>
    ///     Stores the settings without raising <see cref="Changed" /> or the <c>settings.changed</c>
    ///     event. Use it for bookkeeping that should not make clients reload their settings.
    /// </summary>
    public Task<bool> SaveSilentlyAsync(T settings, CancellationToken token = default) =>
        SaveCoreAsync(settings, false, token);

    private async Task<bool> SaveCoreAsync(T settings, bool notify, CancellationToken token)
    {
        // A public save comes from the interface, so the app may keep fields it owns itself (the
        // open tabs, for one). A silent save is the app writing its own bookkeeping, and merging
        // would replace exactly the value it just changed.
        var normalized = NormalizeAll(notify ? Merge(settings) : settings);
        Current = normalized;

        var saved = await _store.SaveAsync(normalized, token);

        if (notify)
        {
            OnSaved(normalized);
            Changed?.Invoke(normalized);
            _notify("settings.changed", Payload());
        }

        return saved;
    }

    /// <summary>
    ///     Merges an incoming save with what is already stored, for fields the interface must not
    ///     own — a window that owns the open tabs, for instance. A public save goes through here; a
    ///     load and a silent bookkeeping save use the value as it is.
    /// </summary>
    protected virtual T Merge(T settings) => settings;

    /// <summary>The payload the interface reads and receives after a save.</summary>
    protected abstract TPayload Payload();

    /// <summary>Applies defaults and ranges; runs on load and before a save.</summary>
    protected virtual T Normalize(T settings) => settings;

    /// <summary>A localized error to reject a save, or null to accept it.</summary>
    protected virtual string? Validate(T settings) => null;

    protected virtual void OnLoaded(T settings)
    {
    }

    protected virtual void OnSaved(T settings)
    {
    }

    /// <summary>Handles an app-specific message; return null for types this service does not own.</summary>
    protected virtual Task<NativeResponse?> HandleExtraAsync(NativeContext context) =>
        Task.FromResult<NativeResponse?>(null);

    /// <summary>Runs the app's <see cref="Normalize" /> and then the generated range checks.</summary>
    private T NormalizeAll(T settings) => (T)SettingsNormalizationRegistry.Normalize(Normalize(settings));

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "settings.get" => Task.FromResult<NativeResponse?>(context.Reply("settings.get.result", Payload())),
        "settings.save" => SaveMessageAsync(context),
        _ => HandleExtraAsync(context)
    };

    private async Task<NativeResponse?> SaveMessageAsync(NativeContext context)
    {
        if (context.Payload<T>() is not { } settings)
            return context.Fail("settings.save.result",
                Localization.Localization.Get("error.invalidSettings"));

        if (Validate(settings) is { } error)
            return context.Fail("settings.save.result", error);

        await SaveAsync(settings);
        return context.Reply("settings.save.result", Payload());
    }
}