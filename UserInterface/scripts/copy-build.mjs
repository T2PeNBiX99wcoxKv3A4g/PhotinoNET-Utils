// Replaces the app's `Resources/wwwroot` with a fresh Vite build.
//
// Run from the app's UserInterface directory (`npm run build`), where Vite wrote `build/` and the
// desktop project reads `../Resources/wwwroot`.

import {cp, rm} from "node:fs/promises";
import path from "node:path";

const uiRoot = process.cwd();
const source = path.join(uiRoot, "build");
const target = path.resolve(uiRoot, "..", "Resources", "wwwroot");

await rm(target, {recursive: true, force: true});
await cp(source, target, {recursive: true});

console.log(`Copied ${source} -> ${target}`);
