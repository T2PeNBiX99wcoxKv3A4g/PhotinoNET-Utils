// Regenerates the TypeScript view of the translation keys with the shared code generator, for a
// build that does not go through MSBuild. A renamed key breaks the build at every call site,
// because i18n.generated.ts augments the bridge's LocalizationKeys interface.
//
// Run from the app's UserInterface directory (`npm run prebuild`). The desktop project's own
// targets write settings.generated.ts from the compiled model after CoreCompile.

import {spawnSync} from "node:child_process";
import {existsSync, readdirSync} from "node:fs";
import path from "node:path";

const uiRoot = process.cwd();
const appRoot = path.resolve(uiRoot, "..");
const binaries = path.join(appRoot, "Libs", "CodeGen");

const windows = process.platform === "win32";
const file = windows ? "PhotinoNETUtils.CodeGen.exe" : "PhotinoNETUtils.CodeGen";
const hostRid = `${windows ? "win" : process.platform === "darwin" ? "osx" : "linux"}-${process.arch}`;

// The host RID's build is the right one; any other is better than failing outright.
const rids = existsSync(binaries) ? readdirSync(binaries) : [];
const rid = rids.includes(hostRid) ? hostRid : rids[0];
const binary = rid ? path.join(binaries, rid, file) : "";

if (!binary || !existsSync(binary)) {
    console.error(`Code generator not found under ${binaries}`);
    process.exit(1);
}

const result = spawnSync(binary, [
    "--i18n", path.join(appRoot, "Resources", "i18n", "en.jsonc"),
    "--i18n-out", path.join(uiRoot, "src", "api", "i18n.generated.ts"),
], {stdio: "inherit"});

process.exit(result.status ?? 1);
