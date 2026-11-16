// Rasterises `og.svg` into the 1200x630 `public/og.png` that the Open Graph tags point at.
//
// The artwork stays a vector in the repository - one file to edit, no binary diff - and the picture
// search engines read is generated, so it always matches it. Runs as part of `npm run generate`, and
// therefore in the site workflow; nothing has to be exported by hand.
//
// Run from the site directory: `node node_modules/@photino-net-utils/site/scripts/generate-og.mjs`.

import {existsSync} from "node:fs";
import {readFile} from "node:fs/promises";
import path from "node:path";

const siteRoot = process.cwd();
const source = path.join(siteRoot, "og.svg");
const target = path.join(siteRoot, "public", "og.png");

if (!existsSync(source)) {
    console.log("No og.svg, leaving public/og.png alone.");
    process.exit(0);
}

const sharp = (await import("sharp")).default;
const svg = await readFile(source);

await sharp(svg, {density: 192})
    .resize(1200, 630, {fit: "contain", background: {r: 0, g: 0, b: 0, alpha: 0}})
    .png({compressionLevel: 9})
    .toFile(target);

console.log("Wrote public/og.png from og.svg");
