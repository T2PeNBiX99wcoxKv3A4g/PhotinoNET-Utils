#!/usr/bin/env node
// Points a "@photino-net-utils/*" dependency at a local tarball.
// usage: set-bridge-dependency.mjs <package.json> <libs-directory> <tarball-name> [package-name]
import {readFileSync, writeFileSync} from "node:fs";
import {dirname, relative, resolve} from "node:path";

const [manifestPath, libsDirectory, tarballName, packageName = "@photino-net-utils/bridge"] =
    process.argv.slice(2);

if (!manifestPath || !libsDirectory || !tarballName) {
    console.error("usage: set-bridge-dependency.mjs <package.json> <libs-directory> <tarball-name> [package-name]");
    process.exit(2);
}

const relativeLibs = relative(dirname(resolve(manifestPath)), resolve(libsDirectory)).replaceAll("\\", "/");
const spec = `file:${relativeLibs}/${tarballName}`;
const text = readFileSync(manifestPath, "utf8");
const escaped = packageName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const pattern = new RegExp(`("${escaped}"\\s*:\\s*")[^"]*(")`);

if (pattern.test(text)) {
    const updated = text.replace(pattern, `$1${spec}$2`);
    if (updated !== text) writeFileSync(manifestPath, updated);
    process.exit(0);
}

// The dependency is not declared yet: add it without disturbing anything else.
const manifest = JSON.parse(text);
manifest.dependencies = {...manifest.dependencies, [packageName]: spec};
writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
