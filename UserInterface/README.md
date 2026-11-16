# @photino-net-utils/bridge

The `window.external` request/response bridge shared by the Photino front-ends, plus the global
`External` type declaration.

It is consumed as a local package, without a build step:

```json
{
  "dependencies": {
    "@photino-net-utils/bridge": "file:../../../PhotinoNETUtils/UserInterface"
  }
}
```

```ts
import {Native} from "@photino-net-utils/bridge";

const info = await Native.request<{version: string}>("app.info");

const off = Native.on((message) => {
    if (message.type === "app.navigate") console.log(message.data);
});
```

`Native.available` is false in a plain browser, so UI code can fall back to browser behaviour
during `npm run dev` without the desktop host.

## Markdown

`Markdown` renders a GitHub release body, or any other Markdown, as HTML. The result is sanitized
with DOMPurify before it reaches the DOM, because the text comes from a release rather than from
the app. The class you pass styles the elements:

```tsx
import {Markdown} from "@photino-net-utils/bridge";

<div className="changelog">
    <Markdown source={release.notes}/>
</div>
```

## Scripts

The build steps every front-end in this family repeats live under `scripts/` in this package, so
an app only names them in its own `package.json`:

```json
{
  "scripts": {
    "prebuild": "node node_modules/@photino-net-utils/bridge/scripts/generate-code.mjs",
    "build": "tsc -b && vite build && node node_modules/@photino-net-utils/bridge/scripts/copy-build.mjs",
    "attributions": "node node_modules/@photino-net-utils/bridge/scripts/generate-attributions.mjs"
  }
}
```

All three are run from the app's `UserInterface` directory and resolve their paths from it.

- **copy-build.mjs** replaces `../Resources/wwwroot` with the Vite `build` output.
- **generate-code.mjs** regenerates `src/api/i18n.generated.ts` from the translation catalogue in
  `../Resources/i18n/en.jsonc` with the CodeGen binary under `../Libs/CodeGen/<rid>`.
- **generate-attributions.mjs** writes `src/data/attributions.ts` with every project the app ships:
  npm packages from `node_modules`, NuGet packages from the `PackageReference` entries and their
  nuspec, Rust crates from every `Cargo.lock`, and whatever `attributions.config.json` adds.

### attributions.config.json

Optional, next to the app's `package.json`. `extras` lists files the app ships but no package
manager knows about; `licences` overrides a package whose nuspec declares nothing. `project`
names a csproj other than the only one in the app directory.

```json
{
  "extras": [
    {"name": "7-Zip", "version": "25.01", "license": "LGPL-2.1-or-later with unRAR restriction", "file": "../Native/licenses/7-Zip.txt"}
  ],
  "licences": {
    "Some.Package": "https://example.com/LICENSE.txt"
  }
}
```

Licence texts that had to be downloaded are cached under `UserInterface/scripts/licenses/` in the
app, which is committed, so later runs and other machines work offline.
