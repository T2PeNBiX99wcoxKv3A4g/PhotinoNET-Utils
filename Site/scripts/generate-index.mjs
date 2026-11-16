// Writes `index.html` from the project's `site.config.mjs`.
//
// The page's metadata is the same everywhere - canonical URL, Open Graph, Twitter card, JSON-LD -
// and only the values differ, so it is generated instead of being copied and edited by hand. The
// file is a build artefact: it is produced by `npm run generate`, which runs before `vite` reads it,
// and the copy that reaches GitHub Pages is the one in `dist`.
//
// Run from the site directory: `node node_modules/@photino-net-utils/site/scripts/generate-index.mjs`.

import {existsSync} from "node:fs";
import {writeFile} from "node:fs/promises";
import path from "node:path";

const siteRoot = process.cwd();
const configPath = path.join(siteRoot, "site.config.mjs");

if (!existsSync(configPath)) {
    throw new Error(`No site.config.mjs in ${siteRoot}; the index page reads its values from it.`);
}

const {site} = await import(`file://${configPath.replace(/\\/g, "/")}?v=${Date.now()}`);

const url = String(site.url ?? "").replace(/\/?$/, "/");
const language = site.locale ?? "en_US";
const name = site.name;
const tagline = site.tagline ?? "";
const description = site.description ?? tagline;
const category = site.category ?? "UtilitiesApplication";
const platforms = site.platforms ?? "Windows, Linux";
const downloadUrl = site.releases ?? `${site.repository}/releases/latest`;
const imageAlt = site.imageAlt ?? `${name} — ${tagline}`;

const escape = (value) =>
    String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;");

const html = `<!doctype html>
<html lang="en">
<head>
    <meta charset="UTF-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
    <meta name="theme-color" content="${escape(site.theme?.background ?? "#0b0f19")}"/>
    <meta name="description" content="${escape(description)}"/>
    <link rel="canonical" href="${escape(url)}"/>
    <link rel="sitemap" type="application/xml" href="./sitemap.xml"/>
    <meta name="robots" content="index,follow"/>
    <meta property="og:locale" content="${escape(language)}"/>

    <meta property="og:type" content="website"/>
    <meta property="og:site_name" content="${escape(name)}"/>
    <meta property="og:title" content="${escape(name)}"/>
    <meta property="og:description" content="${escape(tagline)}"/>
    <meta property="og:url" content="${escape(url)}"/>
    <meta property="og:image" content="${escape(url)}og.png"/>
    <meta property="og:image:type" content="image/png"/>
    <meta property="og:image:width" content="1200"/>
    <meta property="og:image:height" content="630"/>
    <meta property="og:image:alt" content="${escape(imageAlt)}"/>

    <meta name="twitter:card" content="summary_large_image"/>
    <meta name="twitter:title" content="${escape(name)}"/>
    <meta name="twitter:description" content="${escape(tagline)}"/>
    <meta name="twitter:image" content="${escape(url)}og.png"/>
    <meta name="twitter:image:alt" content="${escape(imageAlt)}"/>

    <link rel="icon" type="image/svg+xml" href="./icon.svg"/>
    <title>${escape(name)}</title>

    <script type="application/ld+json">
        {
            "@context": "https://schema.org",
            "@graph": [
                {
                    "@type": "SoftwareApplication",
                    "name": "${escape(name)}",
                    "applicationCategory": "${escape(category)}",
                    "operatingSystem": "${escape(platforms)}",
                    "description": "${escape(description)}",
                    "url": "${escape(url)}",
                    "downloadUrl": "${escape(downloadUrl)}",
                    "inLanguage": "en",
                    "offers": {
                        "@type": "Offer",
                        "price": "0",
                        "priceCurrency": "USD"
                    },
                    "publisher": {
                        "@type": "Organization",
                        "name": "${escape(name)}",
                        "url": "${escape(site.repository)}"
                    }
                },
                {
                    "@type": "WebSite",
                    "name": "${escape(name)}",
                    "url": "${escape(url)}",
                    "inLanguage": "en"
                }
            ]
        }
    </script>
</head>
<body>
<div id="root"></div>
<script type="module" src="/src/main.tsx"></script>
</body>
</html>
`;

await writeFile(path.join(siteRoot, "index.html"), html, "utf8");
console.log("Wrote index.html from site.config.mjs");
