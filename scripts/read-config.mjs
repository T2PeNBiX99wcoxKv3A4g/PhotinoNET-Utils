#!/usr/bin/env node
// Prints one tab-separated row per configured target:
//   name <TAB> absolute target path <TAB> libs directory name <TAB> UI directory name
import {readFileSync} from "node:fs";
import {dirname, resolve} from "node:path";

const configPath = process.argv[2];
if (!configPath) {
    console.error("usage: read-config.mjs <config.json>");
    process.exit(2);
}

const config = JSON.parse(readFileSync(configPath, "utf8"));
const base = dirname(resolve(configPath));
const libsDirectory = config.libsDirectory ?? "Libs";
const userInterfaceDirectory = config.userInterfaceDirectory ?? "UserInterface";
const targets = config.targets;

if (!Array.isArray(targets) || targets.length === 0) {
    console.error("config: 'targets' must be a non-empty array");
    process.exit(2);
}

for (const target of targets) {
    if (!target || typeof target.path !== "string" || target.path.length === 0) {
        console.error("config: every target needs a 'path'");
        process.exit(2);
    }

    const name = target.name ?? target.path;
    const path = resolve(base, target.path).replaceAll("\\", "/");
    console.log([name, path, libsDirectory, userInterfaceDirectory].join("\t"));
}
