using JetBrains.Annotations;
using PhotinoNetUtils.Hosting;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Settings;

namespace PhotinoNetUtils.Services;

/// <summary>
///     Owns one settings model and answers the standard <c>settings.get</c> / <c>settings.save</c>
///     messages, so an app only supplies the payload, its validation and its side effects.
/// </summary>
/// <remarks>
///     <typeparamref name="T" /> is the model read from the store; <typeparamref name="TPayload" /> is
///     the shape the interface reads. Register the payload in the app's serializer context, because
///     the bridge serializes it through that context.
/// </remarks>
[PublicAPI]
public abstract class SettingsServiceBase<T, TPayload> : IDisposable
    where T : class
    where TPayload : class
{
    private readonly ISettingsStore<T> _store;

    protected SettingsServiceBase(ISettingsStore<T> store)
    {
        _store = store;
        Current = store.Current;
        NativeBridge.MessageReceived += HandleMessageAsync;
    }

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
    public async Task<bool> SaveAsync(T settings, CancellationToken token = default)
    {
        var normalized = NormalizeAll(settings);
        Current = normalized;

        var saved = await _store.SaveAsync(normalized, token);

        OnSaved(normalized);
        Changed?.Invoke(normalized);
        WebViewEvents.Notify("settings.changed", Payload());
        return saved;
    }

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