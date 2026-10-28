#!/usr/bin/env node
// Points the "@photino-net-utils/bridge" dependency at a local tarball.
// usage: set-bridge-dependency.mjs <package.json> <libs-directory> <tarball-name>
import {readFileSync, writeFileSync} from "node:fs";
import {dirname, relative, resolve} from "node:path";

const [manifestPath, libsDirectory, tarballName] = process.argv.slice(2);
if (!manifestPath || !libsDirectory || !tarballName) {
    console.error("usage: set-bridge-dependency.mjs <package.json> <libs-directory> <tarball-name>");
    process.exit(2);
}

const relativeLibs = relative(dirname(resolve(manifestPath)), resolve(libsDirectory)).replaceAll("\\", "/");
const spec = `file:${relativeLibs}/${tarballName}`;
const text = readFileSync(manifestPath, "utf8");
const pattern = /("@photino-net-utils\/bridge"\s*:\s*")[^"]*(")/;

if (pattern.test(text)) {
    const updated = text.replace(pattern, `$1${spec}$2`);
    if (updated !== text) writeFileSync(manifestPath, updated);
    process.exit(0);
}

// The dependency is not declared yet: add it without disturbing anything else.
const manifest = JSON.parse(text);
manifest.dependencies = {...manifest.dependencies, "@photino-net-utils/bridge": spec};
writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
