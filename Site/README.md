# @photino-net-utils/site

The landing page and documentation site shared by the desktop front-ends in this family. One
implementation, one place to fix a style bug; a project supplies its colours, its text and its
documentation.

## What a project keeps

```
Site/
  site.config.mjs      name, links, colours, doc pages, languages
  site.icons.tsx       the glyphs its feature and platform lists draw (optional)
  docs/<lang>/*.md     the documentation pages
  og.svg               the share artwork, rasterised into public/og.png
  public/              icon.svg, robots.txt, sitemap.xml
  index.html           the Open Graph and JSON-LD metadata
  src/i18n/*.json      the text of every language
  src/main.tsx         three lines: build the options and call renderSite
  vite.config.ts       a Vite config that excludes the package from pre-bundling
```

## What this package keeps

- `App` - the header, navigation, footer and hash routes.
- `pages/Home`, `pages/Docs`, `pages/Scripts` - the three page layouts.
- The theme (`theme.tsx`), the language context, the release badge, and the text helper.
- `scripts/generate-docs.mjs` - markdown to `src/docs.generated.ts`, for every language.
- `scripts/generate-og.mjs` - `og.svg` to `public/og.png` (1200x630) with sharp.

## Using it

```tsx
// src/main.tsx
import {renderSite} from "@photino-net-utils/site";
import {site} from "../site.config.mjs";
import {icons} from "../site.icons";
import {copy} from "./i18n";
import {docs} from "./docs.generated";

renderSite({config: {...site, icons}, languages: site.languages.map(...), defaultLanguage: "en", copy, docs});
```

`site.config.mjs` is plain JavaScript so the docs generator can read it with node; everything else is
typed by the package.

```js
export const site = {
    name: "AntiBot",
    tagline: "Keep bots out of your VRChat instance.",
    icon: "https://raw.githubusercontent.com/.../icon.svg",
    repository: "https://github.com/.../AntiBot",
    languages: ["en", "ja", "zh-CN", "zh-TW"],
    docs: [{slug: "Getting-Started", group: "setup"}],
    theme: {
        accent: "#a78bfa",
        accentStrong: "#8b5cf6",
        background: "#08070f",
        card: "rgba(22, 19, 38, 0.78)",
        topBar: "rgba(8, 7, 15, 0.82)",
        textPrimary: "#f5f3ff",
        textSecondary: "#a9a2c4",
        textMuted: "#6f6885",
        body: "#d8d3ea",
        border: "rgba(167, 139, 250, 0.12)",
        borderHover: "rgba(167, 139, 250, 0.4)",
    },
};
```

A page can also read a file the project already keeps instead of a copy under `docs`. That is how a
changelog written by the release workflow becomes a page without being duplicated:

```js
docs: [
    {slug: "Getting-Started", group: "setup"},
    {slug: "Changelog", group: "history", source: "../CHANGELOG.md"},
],
```

`source` is relative to the site directory, and a translation at `docs/<language>/Changelog.md` still
wins over it. The body is stored once even when every language shows it. The group needs a label in
`copy.docs` (`"history": "History"`).

## Scripts

The project's `package.json` names them, so a site only repeats three lines:

```json
{
  "scripts": {
    "generate": "node node_modules/@photino-net-utils/site/scripts/generate-docs.mjs && node node_modules/@photino-net-utils/site/scripts/generate-og.mjs",
    "dev": "npm run generate && vite",
    "build": "npm run generate && tsc --noEmit && vite build"
  }
}
```

`vite.config.ts` must keep the package out of the dependency pre-bundler, so its sources are compiled
with the site:

```ts
export default defineConfig({plugins: [react()], base: "./", optimizeDeps: {exclude: ["@photino-net-utils/site"]}});
```
