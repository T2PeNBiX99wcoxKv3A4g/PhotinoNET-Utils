# PhotinoNETUtils

Shared plumbing for Photino.NET desktop apps — the pieces Photino itself does not provide. It is a
plain class library (`net10.0`) plus a small npm package for the front-end bridge.

## Contents

| Area                      | What it provides                                                                                                                                                                                     |
|---------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging`                 | Serilog console + rolling file log, console mirroring, unhandled-exception hooks                                                                                                                     |
| `Hosting/StaticServer`    | Kestrel static file host on an OS-assigned port (`127.0.0.1:0`), serving `Resources/wwwroot` from embedded resources with disk overrides                                                             |
| `Interop`                 | `ITrayIcon` / `TrayIconFactory` with Win32 and libayatana-appindicator implementations, plus the Win32 P/Invoke set                                                                                  |
| `Platform`                | Linux GTK helpers (hide/show/transparency/clipboard/log routing), Linux diagnostics, Windows shortcuts (`.lnk` creation, plus keeping Photino's Start Menu entry in step with the running build), window chrome (drag, resize, maximize, frameless title bar on both platforms) and `SystemLauncher` for handing a URL or folder to the system |
| `Services/SingleInstance` | Lock-file single-instance guard plus named-pipe argument forwarding                                                                                                                                  |
| `AppIcon`                 | Extracts the platform icon (`icon.ico` / `icon.png`) from embedded resources                                                                                                                         |
| `AppPaths`                | Per-user data and log folders, created on demand                                                                                                                                                     |
| `Utils`                   | `Result<T>`, `Try`, `Singleton<T>`                                                                                                                                                                   |
| `Extensions`              | Result/exception/string/object helpers                                                                                                                                                               |
| `UserInterface`           | `@photino-net-utils/bridge`, the shared `window.external` TypeScript bridge                                                                                                                          |

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

using var tray = TrayIconFactory.Create(
    Constants.Name,
    Constants.WindowTitle,
    iconPath,
    ShowMainWindow,
    [
        new TrayMenuItem("Open", ShowMainWindow),
        new TrayMenuItem("Exit", Exit, SeparatorBefore: true)
    ]);

var started = OperatingSystem.IsLinux() && !TrayIconFactory.IsToolkitReady
    ? /* start later, on the GTK thread */ false
    : tray.Start();
```

On Linux the icon can only be created on the GTK thread once the toolkit has a display connection;
poll `TrayIconFactory.IsToolkitReady` first and call `Start()` through `PhotinoWindow.Invoke`.

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

### App paths and the system launcher

```csharp
using PhotinoNetUtils;
using PhotinoNetUtils.Platform;

var data = AppPaths.DataDirectory(Constants.Name);  // %LOCALAPPDATA%/<name>, created on demand
var logs = AppPaths.LogDirectory(Constants.Name);   // %APPDATA%/<name>/logs, the one Logging writes to

SystemLauncher.Open(logs);           // the folder, in the file manager
SystemLauncher.Open("https://…");    // the user's default handler, which may be this app
```

`AppPaths` keeps the two folder layouts in one place, and `Logging.Initialize` uses the same log
folder, so an app never has to guess where its files went.

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

### Front-end bridge

```ts
import {Native} from "@photino-net-utils/bridge";
```

It ships the `Native` helper and the global `window.external` type declaration.

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
