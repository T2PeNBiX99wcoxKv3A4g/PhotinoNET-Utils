# Photino.NET Utils

Shared plumbing for Photino.NET desktop apps — the pieces Photino itself does not provide. It is a
plain class library (`net10.0`) plus a small npm package for the front-end bridge.

## Contents

| Area                      | What it provides                                                                                                                                                                                                                                                                                                                               |
|---------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging`                 | Serilog console + rolling file log, console mirroring, unhandled-exception hooks                                                                                                                                                                                                                                                               |
| `Hosting/StaticServer`    | Kestrel static file host on an OS-assigned port (`127.0.0.1:0`), serving `Resources/wwwroot` from embedded resources with disk overrides                                                                                                                                                                                                       |
| `Interop`                 | `ITrayIcon` / `TrayIconFactory` with Win32 and libayatana-appindicator implementations, plus the Win32 P/Invoke set                                                                                                                                                                                                                            |
| `Platform`                | Linux GTK helpers (hide/show/transparency/clipboard/log routing), Linux diagnostics, Windows shortcuts (`.lnk` creation, plus keeping Photino's Start Menu entry in step with the running build), window chrome (drag, resize, maximize, frameless title bar on both platforms) and `SystemLauncher` for handing a URL or folder to the system |
| `Services/SingleInstance` | Lock-file single-instance guard plus named-pipe argument forwarding                                                                                                                                                                                                                                                                            |
| `AppIcon`                 | Extracts the platform icon (`icon.ico` / `icon.png`) from embedded resources                                                                                                                                                                                                                                                                   |
| `AppPaths`                | Per-user settings, local data and log folders, created on demand                                                                                                                                                                                                                                                                               |
| `Localization`            | Embedded `<prefix><locale>.jsonc` UI strings, merged over the language and the default locale, answered over the bridge as `i18n.get`                                                                                                                                                                                                          |
| `Utils`                   | `Result<T>`, `Try`, `Singleton<T>`                                                                                                                                                                                                                                                                                                             |
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

## Usage

### Static server

```csharp
using PhotinoNetUtils.Hosting;

using var server = StaticServer.Start(DataDirectory);
var appUrl = $"{server.BaseUrl}/index.html?v={buildToken}";
```

The content root is owned by the app, so Kestrel never writes `wwwroot` next to the executable.
Embedded files are read from the entry assembly under `Resources/wwwroot`.

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

### Localization

```csharp
using PhotinoNetUtils.Localization;

// Resources/i18n/en.jsonc, ja.jsonc, zh-TW.jsonc … embedded in the app's assembly.
Localization.Initialize(typeof(Program).Assembly, "MyApp.Resources.i18n.",
    () => SettingsService.Instance.Language);

var label = Localization.Get("tray.open");       // the current language
var german = Localization.Get("de", "tray.open"); // a specific one
```

A locale is merged over its language and then over the default locale, so a partially translated file
still shows something. `Initialize` also answers `i18n.get` on the bridge, so the React side reads the
same text; register `LocaleRequest` and `LocalePayload` in the app's `JsonSerializerContext`.

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

Output: `PhotinoNETUtils/bin/Release/net10.0/PhotinoNETUtils.dll`.

The npm package in `UserInterface` is built with `npm run pack`, which produces
`photino-net-utils-bridge-<version>.tgz`. It is publish-ready:

```bash
cd UserInterface
npm publish
```
