# Photino.NET Utils

Shared plumbing for Photino.NET desktop apps — the pieces Photino itself does not provide. It is a
plain class library (`net11.0`) plus a small npm package for the front-end bridge.

## Contents

| Area                      | What it provides                                                                                                                                                                                                                                                                                                                               |
|---------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging`                 | Serilog console + rolling file log, console mirroring, unhandled-exception hooks                                                                                                                                                                                                                                                               |
| `Hosting/StaticServer`    | Kestrel static file host on an OS-assigned port (`127.0.0.1:0`), serving `Resources/wwwroot` from embedded resources with disk overrides                                                                                                                                                                                                       |
| `Hosting/WebViewCache`    | Drops the web view's data folder when a different build filled it, so an upgrade cannot keep serving the old interface from the cache                                                                                                                                                                                                            |
| `Hosting/WebViewEvents`   | Queues events until the page reports `app.ready`, and answers the standard `window.*` commands from the bridge                                                                                                                                                                                                                                    |
| `Interop`                 | `ITrayIcon` / `TrayIconFactory` with Win32 and libayatana-appindicator implementations, plus the Win32 P/Invoke set                                                                                                                                                                                                                            |
| `Platform`                | Linux GTK helpers (hide/show/transparency/clipboard/log routing), Linux diagnostics, Windows shortcuts (`.lnk` creation, plus keeping Photino's Start Menu entry in step with the running build), window chrome (drag, resize, maximize, frameless title bar on both platforms) and `SystemLauncher` for handing a URL or folder to the system |
| `Services/SingleInstance` | Lock-file single-instance guard plus named-pipe argument forwarding                                                                                                                                                                                                                                                                            |
| `Services/SettingsStore` | `ISettingsStore<T>`, JSON/TOML stores and `SettingsServiceBase<TSelf,TPayload>`: attribute comments, defaults, atomic writes and a one-time legacy move                                                                                                                                                                          |
| `Settings` / `Messaging`  | `[SettingsModel]` / `[Setting]` metadata and the `[NativeMessage]` dispatcher generator                                                                                                                                                                                                                                                       |
| `CodeGen`                 | Small CLI that turns a `[SettingsModel]` file into a TypeScript interface and JSON schema                                                                                                                                                                                                                                                      |
| `Services/StartupService` | Answers `app.startup` / `app.setStartup` and keeps the platform autostart entry in step                                                                                                                                                                                                                                                         |
| `Services/AppInfoService` | Answers `app.info` with the app's version, platform, log folder, rounded corners and debug flag                                                                                                                                                                                                                                                 |
| `AppIcon`                 | Extracts the platform icon (`icon.ico` / `icon.png`) from embedded resources                                                                                                                                                                                                                                                                   |
| `AppPaths`                | Per-user settings, local data and log folders, created on demand                                                                                                                                                                                                                                                                               |
| `Localization`            | Embedded `<prefix><locale>.jsonc` UI strings, merged over the language and the default locale, answered over the bridge as `i18n.get`                                                                                                                                                                                                          |
| `Notifications`           | OS notifications through Photino's native layer, queued until the window exists, with per-key debounce                                                                                                                                                                                                                                          |
| `Utils`                   | `Result<T>`, `Try`, `[Singleton]` and `SingletonRegistry` (`Singleton<T>` is obsolete)                                                                                                                                                                                                                                                      |
| `Extensions`              | Result/exception/string/object helpers                                                                                                                                                                                                                                                                                                         |
| `UserInterface`           | `@photino-net-utils/bridge`: the shared `window.external` TypeScript bridge plus the React localization (`LocalizationProvider`, `useTranslation`, `useLocale`) matching `PhotinoNetUtils.Localization`                                                                                                                                        |

## Consuming the built artifacts

Until this is published as a NuGet package, each consumer keeps a copy of the built artifacts in its
own `Libs` folder:

```
<app>/Libs/PhotinoNETUtils.dll
<app>/Libs/photino-net-utils-bridge-<version>.tgz
```

Reference the DLL directly:

```xml
<ItemGroup>
    <Reference Include="PhotinoNETUtils">
        <HintPath>Libs\PhotinoNETUtils.dll</HintPath>
        <Private>true</Private>
    </Reference>
</ItemGroup>
```

and the npm package from the app's `UserInterface/package.json`:

```json
{
  "dependencies": {
    "@photino-net-utils/bridge": "file:../Libs/photino-net-utils-bridge-0.1.0.tgz"
  }
}
```

`build-and-copy` builds both artifacts and copies them into the configured consumers, updates the
dependency path, and refreshes their `node_modules`.

Create the local config from the example (it contains machine-specific paths, so it is git-ignored):

```bash
cp build-and-copy.config.example.json build-and-copy.config.json
```

```json
{
  "libsDirectory": "Libs",
  "userInterfaceDirectory": "UserInterface",
  "targets": [
    { "name": "MyApp", "path": "../MyApp/MyApp" }
  ]
}
```

`path` is resolved relative to the config file. Then run the script for the platform:

```bash
# Windows
pwsh ./build-and-copy.ps1

# Linux / macOS
./build-and-copy.sh

# MSYS2: run it from the MSYS2 shell (the shebang uses /bin/bash)
./build-and-copy.sh
```

If PowerShell resolves `bash` to the Windows WSL alias instead of MSYS2, call it explicitly:

```powershell
& "$env:USERPROFILE\scoop\apps\msys2\current\usr\bin\bash.exe" ./build-and-copy.sh
```

Both accept `--config <file>` (`-Config` on PowerShell), `--skip-npm` (`-SkipNpm`) and
`--skip-target-install` (`-SkipTargetInstall`).

A `Reference` does not flow package dependencies, so the app still needs:

- `<GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>`
- `Microsoft.Extensions.FileProviders.Embedded` — the embedded manifest is generated by that
  package's build targets, and the manifest provider is also used at runtime
- `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (Kestrel)
- `Serilog`, `Serilog.Sinks.Console`, `Serilog.Sinks.File` if it calls `Logging.Initialize`
- the analyzer, to use `[SettingsModel]` or `[NativeMessage]`:

  ```xml
  <ItemGroup>
      <Analyzer Include="Libs\PhotinoNETUtils.Generators.dll" />
  </ItemGroup>
  ```

## Usage

### Static server

```csharp
using PhotinoNetUtils.Hosting;

using var server = StaticServer.Start(DataDirectory);
var appUrl = $"{server.BaseUrl}/index.html?v={buildToken}";
```

The content root is owned by the app, so Kestrel never writes `wwwroot` next to the executable.
Embedded files are read from the entry assembly under `Resources/wwwroot`.

### Web view cache and events

```csharp
using PhotinoNetUtils;
using PhotinoNetUtils.Hosting;

var webViewData = WebViewCache.Prepare(
    AppPaths.DataDirectory("MyApp"),
    BuildInfo.BuildTokenFor(typeof(Program).Assembly));

var window = new PhotinoWindow()
    .SetTemporaryFilesPath(webViewData);

// After the native window is created, before WaitForClose/Run:
WebViewEvents.Attach(window);
WebViewEvents.Ready += OnFirstRender;

// From any thread, even before the window exists:
WebViewEvents.Notify("auth.changed", payload);
```

`WebViewCache.Prepare` returns the folder to give `SetTemporaryFilesPath`; when the build token
changes it deletes the previous contents first. `WebViewEvents` holds the newest event of each type
until the page sends `app.ready`, then pushes them, and answers `window.minimize`,
`window.toggleMaximize`, `window.close`, `window.drag` and `window.resize` for the bridge's
`useWindowControls`, `useWindowDrag` and `ResizeHandles`. The app keeps its own close-to-tray policy.
Register `WindowStatePayload` and `WindowResizeRequest` in the app's `JsonSerializerContext`.

### Tray icon

```csharp
using PhotinoNetUtils.Interop;

using var tray = TrayIconFactory.CreateStandard(
    Constants.Name,
    Constants.WindowTitle,
    iconPath,
    "Open",
    "Settings",
    "Exit",
    ShowMainWindow,
    ShowSettings,
    Exit);

TrayIconFactory.StartWhenReady(tray, windowManager.RunOnMainWindow, started => closeToTray = started);
```

On Linux the helper waits for GTK's display connection, then starts the icon on the UI thread. On other
platforms it starts the icon immediately.

`CreateStandard` can add a reload action without changing the native tray implementations:

```csharp
TrayIconFactory.CreateStandard(
    appId, title, iconPath, "Open", "Settings", "Exit",
    ShowMainWindow, ShowSettings, Exit,
    onReload: () => _ = recovery.Reload(),
    reloadLabel: "Reload interface");
```

### WebView recovery

Attach the shared watchdog to the main window before loading the app URL. A page that never reports
`app.ready` is reloaded a few times; after that the tray action remains available for a manual retry.

```csharp
using PhotinoNetUtils.Services;

var recovery = WebViewRecovery.Attach(window, appUrl);
window.Load(appUrl);
// Dispose recovery when the app exits. Keep it alive while the window is hidden to the tray.
```

The default waits 20 seconds for the first render. Once the page has reported ready it is left alone:
a missing heartbeat or an empty `#root` never navigates away from a page the user is working in, so
use the tray's Reload entry for that. A reload re-navigates the existing Photino window; it does not
create another window or another WebView profile.

### Shortcuts

```csharp
using PhotinoNetUtils.Platform;

// A per-user autostart entry, next to the app's other per-user state.
WindowsShortcut.Create(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MyApp.lnk"),
    executable,
    arguments: "",
    workingDirectory: Path.GetDirectoryName(executable)!,
    description: "MyApp");

// Photino drops a Start Menu shortcut for a window that registers for notifications and never
// looks at it again, so keep it pointing at the build that is running now.
if (OperatingSystem.IsWindows())
    StartMenuShortcut.Refresh("MyApp");
```

`StartMenuShortcut.Refresh` rewrites the shortcut only when its target differs from the executable,
and creates it when it is missing. It never throws: the failure is logged, because it runs while the
window is being created.

### Notifications

```csharp
using PhotinoNetUtils.Notifications;

// The id Windows attributes the toast to; the Start Menu shortcut carries the same one.
window.SetNotificationRegistrationId("MyApp");

// Once the native window exists (for example in its WindowCreated handler):
NotificationService.Attach(window);

// From any thread, at any time:
NotificationService.Notify("auth.expired", "Session expired", "Sign in again.");
```

A notification raised before `Attach` waits and is delivered as soon as the window exists, so a
background service can announce a startup failure. Repeats of the same key are suppressed for
15 seconds; change `NotificationService.Debounce` to taste. Call `NotificationService.Detach()` when
the app exits. On Windows the native layer registers the AppUserModelID while creating the window, so
the toast is attributed to the app; on Linux it uses libnotify.

### Build metadata and autostart

```csharp
using PhotinoNetUtils;
using PhotinoNetUtils.Platform;

var version = BuildInfo.VersionOf(typeof(Program).Assembly);
var platform = BuildInfo.Platform;
var buildToken = BuildInfo.BuildTokenFor(typeof(Program).Assembly);

var startup = new StartupRegistration(
    typeof(Program).Assembly,
    appName: "MyApp",
    fallbackIcon: "web-browser",
    comment: "MyApp URL intermediary",
    linuxWindowClass: "MyApp");

var enabled = startup.IsEnabled;
startup.Refresh();
startup.SetEnabled(true);
```

`StartupRegistration` creates a Windows Startup shortcut or an executable XDG autostart desktop
entry. The comment, fallback icon and Linux window class are app-specific; omit optional values when
they are not needed. `BuildInfo.BuildTokenFor` uses the executable's write time, falling back to the
provided assembly's version when no executable path is available.

The bridge services wrap the common parts of those two:

```csharp
using PhotinoNetUtils.Services;

using var appInfo = new AppInfoService(new AppInfoOptions(
    typeof(Program).Assembly,
    LogDirectory,
    Debug: Constants.IsDebugMode,
    Rounded: () => windowManager.DrawsWindowCorners));

using var startup = new StartupService(new StartupRegistration(
    typeof(Program).Assembly, "MyApp", "web-browser"));
```

`AppInfoService` answers `app.info`, and `StartupService` answers `app.startup` / `app.setStartup`,
refreshing an existing autostart entry on construction so a moved executable is corrected. Register
`AppInfoPayload`, `StartupRequest` and `StartupPayload` in the app's `JsonSerializerContext`.

### App paths and the system launcher

```csharp
using PhotinoNetUtils;
using PhotinoNetUtils.Platform;

var settings = AppPaths.SettingsDirectory(Constants.Name); // %APPDATA%/<name>, settings and user data
var data = AppPaths.DataDirectory(Constants.Name);         // %LOCALAPPDATA%/<name>, caches and content root
var logs = AppPaths.LogDirectory(Constants.Name);          // %APPDATA%/<name>/logs, the one Logging writes to

SystemLauncher.Open(settings);       // the folder, in the file manager
SystemLauncher.Open("https://…");    // the user's default handler, which may be this app
```

`AppPaths` keeps the roaming/local split in one place: settings and logs live under `%APPDATA%` so
they roam with the user, while the static content root, the extracted icon and other disposable
files stay in `%LOCALAPPDATA%`. `Logging.Initialize` uses the same log folder, so an app never has to
guess where its files went.

### Window chrome

```csharp
if (OperatingSystem.IsWindows())
{
    WindowFrame.RemoveTitleBar(window.WindowHandle);   // keep the frame, drop the title bar
    WindowDrag.Begin(window.WindowHandle);
    WindowResize.Begin(window.WindowHandle, "bottomright");
    WindowZoom.Toggle(window.WindowHandle);
}
else
{
    LinuxWindow.BeginDrag(window.Title);
    LinuxWindow.BeginResize(window.Title, "bottomright");
    LinuxWindow.ToggleMaximize(window.Title);
}
```

The Windows helpers work on a window handle; the GTK ones look the toplevel up by its title, because
Photino exposes no widget handle on Linux. Both hand the actual work to the compositor, which is what
makes the move and resize work under Wayland.

### Resizing on Wayland

Those helpers start a drag, which the user finishes. Changing a window's size from the application
is different: `gtk_window_resize` is only a hint, and a compositor may ignore it — Hyprland does for
a floating window, which then keeps whatever size it was mapped with. Hyprland can do the resize
itself, and its command socket is `hyprctl` without spawning it:

```csharp
// A popup that should be exactly as tall as its content.
if (LinuxHyprland.Resize(window.Title, width, height))
    return;
```

It returns false on any other desktop, so the usual `window.SetSize()` + `window.Center()` path can
stay behind it untouched. The window is centred again as well, since a compositor-side resize grows
it from the top-left corner.

The same socket answers questions, so a window that has to appear next to the pointer — which
Wayland hides from clients — can be put there as well:

```csharp
// A button that pops up where the pointer is, e.g. after a copy.
if (LinuxHyprland.MoveNextToCursor(window.Title, offset: 16, size: 52))
    return;
```

It asks for `cursorpos` and `monitors`, keeps the window inside the monitor the pointer is on, and
moves it with `hl.dsp.window.move()`.

### Termination signals

```csharp
using PhotinoNetUtils.Platform;

using var termination = TerminationSignals.Handle(windowManager.Exit);
windowManager.Run();
```

On Linux the main thread sits inside Photino's native loop, so the runtime's default SIGTERM handling
cannot close the window. `TerminationSignals` routes SIGTERM, SIGINT and SIGHUP to the app's own exit
path; a second signal forces the process down if closing hangs. It is a no-op on Windows, so the same
call is safe everywhere.

### Settings files

```csharp
using PhotinoNetUtils.Services;
using PhotinoNetUtils.Settings;

// The attribute supplies the comment written above the key.
[SettingsModel]
public sealed record MySettings
{
    [Setting(Comment = "The folder MyApp follows.")]
    public string Folder { get; init; } = "";

    [Setting(Comment = "Start with the window hidden.")]
    public bool StartHidden { get; init; }
}

// TOML, with a comment block and one comment per annotated property.
var toml = new TomlSettingsStore<MySettings>(
    Path.Combine(AppPaths.SettingsDirectory("MyApp"), "settings.toml"),
    MySettings.Default,
    MyTomlContext.Default.MySettings,
    legacyReader: text => JsonSerializer.Deserialize(text, MyJsonContext.Default.MySettings),
    legacyPath: Path.Combine(AppPaths.SettingsDirectory("MyApp"), "settings.json"),
    header: "# MyApp settings. Rewritten when settings are saved.");

ISettingsStore<MySettings> store = toml;
var settings = await store.LoadAsync();
await store.SaveAsync(settings);
```

`[Setting("text")]` is shorthand for `[Setting(Comment = "text")]`. The generator and the TypeScript
CLI both accept either spelling, and neither cares how the attribute is laid out.

`SettingsServiceBase<TSelf, T, TPayload>` answers `settings.get` / `settings.save`, raises `Changed`
and owns the process-wide `Instance`; the app only supplies the payload, its validation and its side
effects:

```csharp
internal sealed class SettingsService
    : SettingsServiceBase<SettingsService, MySettings, SettingsPayload>
{
    public SettingsService() : base(store) { }

    protected override SettingsPayload Payload() => ...;
    protected override MySettings Normalize(MySettings settings) => settings with { ... };
    protected override string? Validate(MySettings settings) => ...;
    protected override void OnSaved(MySettings settings) { ... }
}

await SettingsService.Instance.LoadAsync();
```

`Instance` is created on first use, like `Singleton<T>`. The constructor has to be public for the
`new()` constraint, so the base also rejects a second instance: `new SettingsService()` throws and
points at `Instance`.

A missing or unreadable file returns the defaults; writes go through a temporary file and a move, so
a crash midway cannot truncate the file. `legacyPath` is read once when the target does not exist
yet, which is how a JSON settings file becomes TOML. The model, its validation and the bridge messages
stay in the app; register the model and the payload in the app's serializer context. TOML serialization
writes the configured `header` and each `[Setting]` comment; comments typed into the file are replaced
by the next save. The annotated properties are marked for trimming, so the comments also survive a
trimmed release build.

### Source generators

`PhotinoNETUtils.Generators.dll` adds three incremental generators:

```csharp
[SettingsModel]
public sealed record MySettings
{
    [Setting(Comment = "The folder MyApp follows.")]
    public string Folder { get; init; } = "";
}

[Singleton]
internal sealed partial class MyLinkService
{
    [NativeMessage("system.openUrl")]
    private static NativeResponse Open(NativeContext context) => ...;
}
```

`[SettingsModel]` registers the comments at startup, so `TomlSettingsStore` does not need reflection
for that model. `[Singleton]` writes `Instance`, the `SingletonRegistry` entry, a `Log` field, seals
the class and adds a private parameterless constructor when the class has none. Write the class
without `sealed`; the generated part seals it, and `PNU3006` warns when the modifier is repeated.
Every constructor of a `[Singleton]` class must be private; the generator reports `PNU3005`
otherwise. `Create()` lives inside the class, so it can still call them, and `new` becomes a compile
error for the rest of the app. Because the members are generated into the class, it can inherit any
base class, which `Singleton<T>` cannot offer. An abstract class or a record is rejected (`PNU3002`),
because neither can be a singleton. `Singleton<T>` is obsolete: it still works for code that already
derives from it, but new classes use the attribute.

`[NativeMessage]` makes the class implement `INativeMessageHandler`; the generator writes the
`HandleAsync` switch over every annotated method, so the class holds no generated method name. A
generated singleton subscribes itself in `Create()`; a plain class subscribes once with
`NativeBridge.Subscribe(this)` and disposes the token. Handlers take one `NativeContext` and return
`NativeResponse?` or `Task<NativeResponse?>`. The class must be `partial`; duplicate message types are
a compile error.

### Singleton registry

`[Singleton]` adds every instance to `SingletonRegistry` while it is constructed, in creation
order; no reflection is involved:

```csharp
foreach (var handler in SingletonRegistry.OfType<INativeMessageHandler>())
    NativeBridge.Subscribe(handler);

var database = SingletonRegistry.Get<Database>();   // throws when it was never created
var player = SingletonRegistry.Find<PlayerService>(); // null instead
```

`All` returns everything and `Count` says how many. A second instance is rejected: a class without
its own constructor gets a private one from the generator, so `new` does not compile, and a class
with its own public constructor is stopped by the registry at run time. The registry belongs to the
library, so one process shares it between the app and every library it loads; types are the keys, so
equal type names in different namespaces do not collide.

A subscription call inside an arbitrary constructor cannot be generated: C# has no partial
constructors, a generator cannot add a base class, and a field initializer cannot reference `this`.
That is the same trade-off as Avalonia's generated `InitializeComponent()` — the generator writes the
body, the constructor calls it once. Reflection could scan for handlers instead, but the release
build is trimmed, so the metadata a scan needs may be gone; the one explicit call is the price of
staying reflection-free.

`[Setting(Min = ..., Max = ...)]` also emits a range check that
`SettingsServiceBase` runs after the app's own `Normalize`, so a hand-edited file cannot leave a
numeric setting outside its range:

```csharp
[Setting(Comment = "Port VRChat listens on for OSC.", Min = 1, Max = 65535)]
public int OscPort { get; init; } = 9000;
```

### Analyzers

The same DLL carries three analyzers, and the localization generator:

| Id | Severity | Finds |
| --- | --- | --- |
| `PNU2001` | warning | a payload passed to `Payload<T>()`, `Reply`, `SerializeEvent`, `WebViewEvents.Notify` or `NativeResponse.Success` that is missing `[JsonSerializable]`; a trimmed build would otherwise throw at run time |
| `PNU1201` | warning | a `[SettingsPayload(typeof(MySettings))]` record without a property for one of the model's `[Setting]` entries, so a new setting never reaches the interface |
| `PNU2101` | warning | `Localization.Get("…")` with a key that is not in the translation files |
| `PNU2102` | **error** | the same key declared twice in one `.jsonc` file, ignoring case |

`PNU2102` is an error on purpose. Keys are case-insensitive in the backend, the generated
constants and the React translator, so a second spelling of a key would never be read: the loader
keeps the first entry. Fix it by renaming or removing the duplicate in the `.jsonc` file. Do not
delete or weaken the rule to make a build pass — the duplicate would come back as a silently
missing translation.

The two localization features read the files added as `AdditionalFiles`:

```xml
<ItemGroup>
    <Analyzer Include="Libs\PhotinoNETUtils.Generators.dll" />
    <AdditionalFiles Include="Resources\i18n\*.jsonc" />
</ItemGroup>
```

They then generate `LocalizationKeys` in the project's root namespace, so a call site can be
`Localization.Get(LocalizationKeys.TrayOpen)` instead of a string that may not exist.

### TypeScript settings types

A Roslyn source generator can only add C#, so the TypeScript side is a small CLI. It is published as
one framework-dependent single-file executable per platform, so a Windows machine can cross-publish
the Linux one; native AOT cannot cross-compile. The file only runs on a build machine, which always
has the runtime:

```bash
dotnet publish PhotinoNETUtils.CodeGen -c Release -r win-x64 -o out
out/PhotinoNETUtils.CodeGen.exe \
  --assembly ../AntiBot/AntiBot/bin/Release/net11.0/AntiBot.dll \
  --out ../AntiBot/AntiBot/UserInterface/src/api/settings.generated.ts \
  --schema ../AntiBot/AntiBot/UserInterface/src/api/settings.schema.json \
  --i18n ../AntiBot/AntiBot/Resources/i18n/en.jsonc \
  --i18n-out ../AntiBot/AntiBot/UserInterface/src/api/i18n.generated.ts
```

It reads the compiled model and writes a `{Model}Generated` interface plus a JSON schema with the
comments and numeric limits; with `--i18n` it also writes the `LocalizationKey` union of every
translated key.

The model is read from metadata, where `#if`, partial files, `const` references and `nameof` are
already resolved, so the interface cannot disagree with the running app. That also means it runs
after the C# compile and a failed compile produces no new files. Two settings sharing one key, or one
assembly declaring two `[SettingsModel]` types, stop the build. `build-and-copy` publishes `win-x64`
and `linux-x64` and copies both to `Libs/CodeGen/<rid>/`, so a fresh checkout has the executable for
either build machine. An app runs the one that matches its host from an MSBuild target:

```xml
<PropertyGroup>
    <SettingsCodeGenPath>$(MSBuildProjectDirectory)\Libs\CodeGen\$(NETCoreSdkRuntimeIdentifier)\PhotinoNETUtils.CodeGen</SettingsCodeGenPath>
    <SettingsCodeGenPath Condition="$([MSBuild]::IsOSPlatform('Windows'))">$(SettingsCodeGenPath).exe</SettingsCodeGenPath>
</PropertyGroup>

<!-- The keys are not a build product; the file is read directly. -->
<Target Name="GenerateLocalizationKeys" BeforeTargets="BeforeBuild"
        Condition="Exists('$(SettingsCodeGenPath)')"
        Inputs="$(MSBuildProjectDirectory)\Resources\i18n\en.jsonc;$(SettingsCodeGenPath)"
        Outputs="$(MSBuildProjectDirectory)\UserInterface\src\api\i18n.generated.ts">
    <Exec Command="&quot;$(SettingsCodeGenPath)&quot; --i18n &quot;$(MSBuildProjectDirectory)\Resources\i18n\en.jsonc&quot; --i18n-out &quot;$(MSBuildProjectDirectory)\UserInterface\src\api\i18n.generated.ts&quot;"/>
</Target>

<!-- The settings come from the compiled model, so the interface cannot disagree with the app. -->
<Target Name="GenerateSettingsTypes" AfterTargets="CoreCompile"
        Condition="Exists('$(SettingsCodeGenPath)')"
        Inputs="@(IntermediateAssembly);$(SettingsCodeGenPath)"
        Outputs="$(MSBuildProjectDirectory)\UserInterface\src\api\settings.generated.ts;$(MSBuildProjectDirectory)\UserInterface\src\api\settings.schema.json">
    <Exec Command="&quot;$(SettingsCodeGenPath)&quot; --assembly &quot;@(IntermediateAssembly)&quot; --out &quot;$(MSBuildProjectDirectory)\UserInterface\src\api\settings.generated.ts&quot; --schema &quot;$(MSBuildProjectDirectory)\UserInterface\src\api\settings.schema.json&quot;"/>
</Target>
```

The `Inputs`/`Outputs` keep the targets from running again when nothing changed; a build that only
touches the translation file still refreshes the key union.

### Localization

```csharp
using PhotinoNetUtils.Localization;

// Resources/i18n/en.jsonc, ja.jsonc, zh-TW.jsonc … embedded in the app's assembly.
Localization.Initialize(typeof(Program).Assembly, "MyApp.Resources.i18n.",
    () => SettingsService.Instance.Language);

var label = Localization.Get("tray.open");       // the current language
var german = Localization.Get("de", "tray.open"); // a specific one
var stored = Localization.Normalize("ja-JP");    // "ja", because that is what the app ships
```

`Normalize` keeps `system`, returns a shipped locale in its canonical spelling, and falls back to the
default for anything the app does not ship. Call `Initialize` first: that is what discovers the
available locales.

A locale is merged over its language and then over the default locale, so a partially translated file
still shows something. `Initialize` also answers `i18n.get` on the bridge, so the React side reads the
same text; register `LocaleRequest` and `LocalePayload` in the app's `JsonSerializerContext`.

Keys are case-insensitive everywhere: `Localization.Get("TRAY.OPEN")`, the generated constants and
`useTranslation()` all resolve the same entry. One file may therefore not declare the same key twice
in different spellings; `PNU2102` fails the build, and the loader keeps the first entry. Fix that by
renaming or removing the key in the `.jsonc` file, not by disabling the check.

A key is a path, not a sentence: lowercase letters, digits and `_`, joined by `.`, as in
`ui.section.open_key`. A text that reads like a translation (`"Account created today"`) is an error
(`PNU2103`), because every call site would become a copy of the sentence and a reword would touch
the code. The C# side turns the path into nested classes and the `_` into a capital:

```csharp
Localization.Get("ui.section.open_key");     // the runtime string
LocalizationKeys.Ui.Section.OpenKey          // the same key, checked by the compiler
```

`ui.section.open_key` becomes `LocalizationKeys.Ui.Section.OpenKey`, so a segment groups its keys
instead of repeating a prefix in every name. The front end is untouched: it keeps the literal key.

### Front-end bridge

```ts
import {Native, LocalizationProvider, useTranslation, useLocale, loadStrings} from "@photino-net-utils/bridge";
```

It ships the `Native` helper, the global `window.external` type declaration, and the React
localization that goes with `PhotinoNetUtils.Localization`:

```tsx
<LocalizationProvider>
    <App/>
</LocalizationProvider>

const t = useTranslation();   // t("tray.open") -> the string, or the key when it is missing
const locale = useLocale();   // "ja", for locale-aware formatting
```

The provider asks the backend for `i18n.get` on mount, reloads on `settings.changed`, and keeps
`<html lang>` in sync. `loadStrings(locale?)` and `createTranslator(strings)` are exported for the
cases that need the table directly. The package has `react >= 18` as a peer dependency.

### Front-end window chrome

An app that hides its native title bar draws one instead:

```tsx
const controls = useWindowControls();   // {maximized, minimize, toggleMaximize, close}
const drag = useWindowDrag();           // spread on the strip that moves the window

<header>
    <Brand {...drag}/>
    <DragArea {...drag}/>
    <button onClick={controls.minimize} title="Minimize"/>
    <button onClick={controls.toggleMaximize} title={controls.maximized ? "Restore" : "Maximize"}/>
    <button onClick={controls.close} title="Close"/>
</header>

<ResizeHandles/>                        // a chromeless window has no frame to grab
```

The hooks send `window.minimize` / `window.toggleMaximize` / `window.close` / `window.drag` and track
`window.state`; `ResizeHandles` sends `window.resize` with the edge. The host answers those with
`WindowFrame`, `WindowDrag`, `WindowZoom`, `WindowResize` and `LinuxWindow`, and must register
`WindowStatePayload` and `WindowResizeRequest` in its `JsonSerializerContext`.

### Corners on Linux

Windows rounds and frames the window itself, because `WindowFrame` keeps the native frame and only
drops the non-client area. GTK has no such middle ground: an undecorated window is a plain square
with no border, so the interface draws both — which needs a transparent window behind them:

```csharp
// Linux, before the window is shown:
window.SetChromeless(true);
var rounded = LinuxWindow.EnableTransparency(window.Title);   // false when the screen cannot
```

Report `rounded` to the interface (in `app.info`), have it paint `border-radius` and an inset border,
and drop the radius while the window is maximized. `LinuxWindow.CanPaintCorners` answers the same
question before the window exists.

## Releasing

- Push to `master` runs **Release**; push to `dev/**` runs **Pre Release**.
- A release first bumps the version in `PhotinoNETUtils/PhotinoNETUtils.csproj`, commits the bump
  plus `CHANGELOG.md`, and creates the tag via
  [Release-Workflows](https://github.com/T2PeNBiX99wcoxKv3A4g/Release-Workflows). Extra version
  files are listed in `extra_version_files` and updated with `sync-version` (append `|<key>` to
  limit the replacement, e.g. `UserInterface/package.json|version`).
- The build job produces:
    - `PhotinoNETUtils.<version>.nupkg` (NuGet package)
    - `photino-net-utils-bridge-<version>.tgz` (npm package)
    - `PhotinoNETUtils.dll` (the raw DLL for consumers using `libs/`)
- The packages are attached to the GitHub Release. Setting `NUGET_API_KEY` / `NPM_TOKEN`
  repository secrets additionally publishes to nuget.org / npmjs.org; without them the publish
  steps are skipped. Pre-releases use the npm `next` tag.
- The packaging steps live in the shared
  `csharp_npm_release.yml` workflow in
  [Release-Workflows](https://github.com/T2PeNBiX99wcoxKv3A4g/Release-Workflows). The C# and npm
  builds themselves are separate reusable workflows (`csharp_library_build.yml`, `npm_build.yml`).

## Build

```bash
dotnet build -c Release
```

Output: `PhotinoNETUtils/bin/Release/net11.0/PhotinoNETUtils.dll`.

The npm package in `UserInterface` is built with `npm run pack`, which produces
`photino-net-utils-bridge-<version>.tgz`. It is publish-ready:

```bash
cd UserInterface
npm publish
```
