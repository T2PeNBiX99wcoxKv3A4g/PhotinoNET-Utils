// Collects the open-source projects an app ships and writes them to `src/data/attributions.ts`,
// which the notices dialog renders.
//
// Run from the app's UserInterface directory (`npm run attributions`), so every path below is
// relative to it:
//
//   package.json                          npm packages, read out of node_modules
//   ../<App>.csproj                       NuGet packages, whose nuspec declares the licence
//   ../**/Cargo.lock                      Rust crates, read out of ~/.cargo/registry
//   attributions.config.json              optional: extras and per-package licence overrides
//   scripts/licenses/                     cached licence texts, committed so later runs are offline
//
// An npm package carries its licence as a file. A NuGet package usually only declares an SPDX
// expression, and so does a crate, so the canonical text of that expression is fetched from
// spdx.org once and cached.

import {existsSync} from "node:fs";
import {mkdir, readFile, readdir, stat, writeFile} from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const uiRoot = process.cwd();
const appRoot = path.resolve(uiRoot, "..");
const configPath = path.join(uiRoot, "attributions.config.json");
const licenseCache = path.join(uiRoot, "scripts", "licenses");
const outputPath = path.join(uiRoot, "src", "data", "attributions.ts");
const nugetRoot = path.join(os.homedir(), ".nuget", "packages");

const isLicenseName = (name) => /^(licen[cs]e|copying|ofl|unlicense)([-._]|$)/i.test(name);
const isNoticeName = (name) => /^(authors|copyright|notice)([-._]|$)/i.test(name);

/** The first licence-looking file in a directory, or null. */
async function findLicenseFile(directory) {
    const name = (await readdir(directory).catch(() => [])).find(isLicenseName);
    return name ? path.join(directory, name) : null;
}

/** Every licence-looking file in a directory, in a stable order. */
async function findLicenseFiles(directory) {
    const names = (await readdir(directory).catch(() => [])).filter(isLicenseName).sort();
    return names.map((name) => path.join(directory, name));
}

/** The copyright year range a statement mentions, such as "2000-2026". */
function copyrightYears(statement) {
    const years = statement.match(/(?:19|20)\d{2}/g);
    if (!years) return "";
    return years[0] === years.at(-1) ? years[0] : `${years[0]}-${years.at(-1)}`;
}

/** The newest build year among a package's files, for packages that never print a year. */
async function newestYear(directory) {
    let newest = 0;

    for (const entry of await readdir(directory, {recursive: true, withFileTypes: true}).catch(() => [])) {
        if (!entry.isFile()) continue;
        const {mtime} = await stat(path.join(entry.parentPath, entry.name)).catch(() => ({mtime: new Date(0)}));
        newest = Math.max(newest, mtime.getFullYear());
    }

    return newest > 1990 ? String(newest) : "";
}

/**
 * A published licence text leaves its copyright line as a template — "Copyright (c) <year>
 * <copyright holders>" — for whoever ships the package to fill in. The package's own metadata
 * answers that, so the notices never show a placeholder.
 */
function fillCopyright(text, {holders, years, program}) {
    if (!/<(?:year|owner|copyright holders?|name of author|program)>/i.test(text)) return text;

    return text
        .replace(/<(?:year)>/gi, years)
        .replace(/<(?:owner|copyright holders?)>/gi, holders)
        .replace(/<name of author>/gi, holders)
        .replace(/<program>/gi, program)
        .replace(/[ \t]{2,}/g, " ")
        .replace(/[ \t]+$/gm, "");
}

/** Downloads a licence text once and caches it in the repository. */
async function remoteText(url, cacheName) {
    const cached = path.join(licenseCache, `${cacheName}.txt`);
    if (existsSync(cached)) return (await readFile(cached, "utf8")).trim();

    const response = await fetch(url);
    if (!response.ok) throw new Error(`Cannot download ${url}: HTTP ${response.status}`);

    const text = (await response.text()).trim();
    await mkdir(licenseCache, {recursive: true});
    await writeFile(cached, `${text}\n`, "utf8");
    console.log(`cached scripts/licenses/${cacheName}.txt`);
    return text;
}

/** Canonical text for an SPDX id, fetched once from spdx.org and cached in the repository. */
async function spdxText(id) {
    // "Apache-2.0 WITH LLVM-exception" and "MIT+" still have a canonical text under their base id.
    const base = id.replace(/\+$/, "").replace(/\s+WITH\s+.*$/i, "").trim();
    if (!base) return "";

    const cached = path.join(licenseCache, `${base}.txt`);
    if (existsSync(cached)) return (await readFile(cached, "utf8")).trim();

    const response = await fetch(`https://spdx.org/licenses/${encodeURIComponent(base)}.json`);
    if (!response.ok) {
        console.warn(`spdx.org has no ${base}, left without text`);
        return "";
    }

    const {licenseText} = await response.json();
    if (!licenseText) return "";

    await mkdir(licenseCache, {recursive: true});
    await writeFile(cached, `${licenseText.trim()}\n`, "utf8");
    console.log(`cached scripts/licenses/${base}.txt`);
    return licenseText.trim();
}

/** The canonical texts of every id in an SPDX expression such as "MIT OR Apache-2.0". */
async function expressionTexts(expression) {
    const ids = expression
        .split(/\s+(?:OR|AND|WITH)\s+/i)
        .map((part) => part.replace(/[()]/g, "").trim())
        .filter(Boolean);

    const texts = [];
    for (const id of ids) {
        const text = await spdxText(id);
        if (text) texts.push(text);
    }

    return texts.join(`\n\n${"-".repeat(72)}\n\n`);
}

/** Guesses the SPDX id of a licence text, for packages that only link to one. */
const KNOWN_LICENSES = [
    [/MIT License/i, "MIT"],
    [/Apache License\s+Version 2\.0/i, "Apache-2.0"],
    [/SIL OPEN FONT LICENSE Version 1\.1/i, "OFL-1.1"],
    [/BSD 3-Clause/i, "BSD-3-Clause"],
    [/BSD 2-Clause/i, "BSD-2-Clause"],
    [/ISC License/i, "ISC"],
];

const detectLicense = (text) => KNOWN_LICENSES.find(([pattern]) => pattern.test(text))?.[1] ?? "";

/** Every npm package the app builds or ships with. */
async function npmEntries() {
    const manifest = JSON.parse(await readFile(path.join(uiRoot, "package.json"), "utf8"));
    const names = [...Object.keys(manifest.dependencies ?? {}), ...Object.keys(manifest.devDependencies ?? {})];
    const entries = [];

    for (const name of names.sort()) {
        const directory = path.join(uiRoot, "node_modules", name);
        const file = await findLicenseFile(directory);
        if (!file) {
            console.warn(`no licence file for ${name}, skipped`);
            continue;
        }

        const own = JSON.parse(await readFile(path.join(directory, "package.json"), "utf8"));
        const author = typeof own.author === "string" ? own.author : own.author?.name ?? "";
        const text = (await readFile(file, "utf8")).trim();

        entries.push({
            name,
            version: own.version ?? "",
            license: typeof own.license === "string" ? own.license : "",
            text: fillCopyright(text, {holders: author || name, years: "", program: name}),
        });
    }

    return entries;
}

/** The app's project file: the one csproj at the root, unless the config names another. */
async function findProjectFile(config) {
    if (config.project) return path.resolve(uiRoot, config.project);

    const names = (await readdir(appRoot)).filter((name) => name.endsWith(".csproj")).sort();
    return names.length > 0 ? path.join(appRoot, names[0]) : null;
}

/**
 * Packages whose nuspec declares no licence at all. Their text is fetched from the project instead;
 * the list stays short and explicit because it should only hold packages that give us no choice.
 */
const DEFAULT_OVERRIDES = {
    "securifybv.ShellLink": "https://raw.githubusercontent.com/securifybv/ShellLink/master/LICENSE.txt",
};

/** Every NuGet package referenced by the desktop project. */
async function nugetEntries(projectPath, overrides) {
    if (!projectPath) {
        console.warn(`no csproj under ${appRoot}, NuGet packages skipped`);
        return [];
    }

    const project = await readFile(projectPath, "utf8");
    const entries = [];

    for (const match of project.matchAll(/<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"/g)) {
        const [, id, version] = match;
        const directory = path.join(nugetRoot, id.toLowerCase(), version);
        const nuspec = (await readdir(directory).catch(() => [])).find((name) => name.endsWith(".nuspec"));
        if (!nuspec) {
            console.warn(`no cached package for ${id} ${version}, skipped`);
            continue;
        }

        const spec = await readFile(path.join(directory, nuspec), "utf8");
        const authors = spec.match(/<authors>([^<]*)<\/authors>/)?.[1]?.trim() ?? "";
        const projectUrl = spec.match(/<projectUrl>([^<]*)<\/projectUrl>/)?.[1]?.trim() ?? "";
        const statement = spec.match(/<copyright>([^<]*)<\/copyright>/)?.[1]?.trim() ?? "";
        const expression = spec.match(/<license type="expression">([^<]*)<\/license>/)?.[1]?.trim();
        const fileName = spec.match(/<license type="file">([^<]*)<\/license>/)?.[1]?.trim();

        let license = expression ?? "";
        let text;

        if (overrides[id]) {
            text = await remoteText(overrides[id], id);
            license = detectLicense(text) || license;
        } else if (fileName) {
            const file = path.join(directory, fileName.replace(/\\/g, path.sep));
            text = existsSync(file) ? (await readFile(file, "utf8")).trim() : "";
        } else if (expression) {
            text = await expressionTexts(expression);
        } else {
            // Older packages only link their licence, so download it and name it from the text.
            const url = spec.match(/<licenseUrl>([^<]*)<\/licenseUrl>/)?.[1]?.trim();
            text = url ? await remoteText(url, id) : "";
            license = detectLicense(text);
        }

        text = fillCopyright(text, {
            holders: authors || id,
            years: copyrightYears(statement) || await newestYear(directory),
            program: id,
        });

        const header = [
            license && `Licensed under ${license}.`,
            authors && `Authors: ${authors}`,
            projectUrl && `Project: ${projectUrl}`,
        ]
            .filter(Boolean)
            .join("\n");

        entries.push({name: id, version, license, text: `${header}\n\n${text}`.trim()});
    }

    return entries;
}

/** Every Cargo.lock the app builds from, without walking the build output. */
async function findCargoLocks(directory, depth = 0) {
    if (depth > 4) return [];

    const skip = new Set(["node_modules", "bin", "obj", "target", ".git", ".temp", "publish"]);
    const found = [];

    for (const entry of await readdir(directory, {withFileTypes: true}).catch(() => [])) {
        if (entry.isFile() && entry.name === "Cargo.lock")
            found.push(path.join(directory, entry.name));
        else if (entry.isDirectory() && !skip.has(entry.name))
            found.push(...await findCargoLocks(path.join(directory, entry.name), depth + 1));
    }

    return found;
}

/** Where cargo unpacked its crate sources: CARGO_HOME first, then the usual installs. */
async function cargoSources() {
    const homes = [
        process.env.CARGO_HOME,
        path.join(os.homedir(), ".cargo"),
        path.join(os.homedir(), ".rustup", "cargo"),
        path.join(os.homedir(), "scoop", "persist", "rustup", ".cargo"),
    ].filter(Boolean);

    for (const home of homes) {
        const directory = path.join(home, "registry", "src");
        if ((await readdir(directory).catch(() => [])).length > 0) return directory;
    }

    return null;
}

/** The licence a crate declares, and every licence text its package ships. */
async function crateLicense(registry, name, version) {
    const indexes = await readdir(registry).catch(() => []);
    let directory = null;

    for (const index of indexes) {
        const candidate = path.join(registry, index, `${name}-${version}`);
        if (existsSync(candidate)) {
            directory = candidate;
            break;
        }
    }

    if (!directory) {
        console.warn(`no cached source for ${name} ${version}, skipped`);
        return null;
    }

    const manifest = await readFile(path.join(directory, "Cargo.toml"), "utf8").catch(() => "");
    const declared = manifest.match(/^\s*license\s*=\s*"([^"]*)"/m)?.[1]?.trim() ?? "";
    const namedFile = manifest.match(/^\s*license-file\s*=\s*"([^"]*)"/m)?.[1]?.trim();
    const authors = manifest.match(/^\s*authors\s*=\s*\[([^\]]*)\]/m)?.[1]
        ?.split(",")
        .map((author) => author.trim().replace(/^"|"$/g, ""))
        .filter(Boolean)
        .join(", ") ?? "";

    const fill = (text) => fillCopyright(text, {holders: authors || name, years: "", program: name});
    const files = (await findLicenseFiles(directory)).concat(
        // Some crates keep the whole statement in AUTHORS instead of a LICENSE file.
        (await readdir(directory).catch(() => [])).filter(isNoticeName).sort()
            .map((entry) => path.join(directory, entry))
    );

    if (files.length > 0) {
        const texts = [];
        for (const file of files) texts.push(fill((await readFile(file, "utf8")).trim()));
        return {
            license: declared || detectLicense(texts[0]),
            text: texts.join(`\n\n${"-".repeat(72)}\n\n`),
        };
    }

    // No file: the declaration is all there is, so the canonical texts stand in for it.
    if (namedFile) {
        const file = path.join(directory, namedFile);
        const text = existsSync(file) ? fill((await readFile(file, "utf8")).trim()) : "";
        if (text) return {license: declared || detectLicense(text), text};
    }

    return {license: declared, text: fill(await expressionTexts(declared))};
}

/** Every crate a Cargo.lock pins, from this repository or not. */
async function cargoEntries() {
    const registry = await cargoSources();
    const locks = await findCargoLocks(appRoot);

    if (!registry) {
        if (locks.length > 0) console.warn("no cargo registry sources found, Rust crates skipped");
        return [];
    }

    const entries = [];
    const seen = new Set();

    for (const lock of locks) {
        const text = await readFile(lock, "utf8").catch(() => "");

        for (const block of text.split("[[package]]").slice(1)) {
            const name = block.match(/^\s*name\s*=\s*"([^"]+)"/m)?.[1];
            const version = block.match(/^\s*version\s*=\s*"([^"]+)"/m)?.[1];
            const source = block.match(/^\s*source\s*=\s*"([^"]+)"/m)?.[1] ?? "";

            // A crate without a source is this repository's own code, not a dependency.
            if (!name || !version || !source.includes("registry+")) continue;

            const key = `${name}@${version}`;
            if (seen.has(key)) continue;
            seen.add(key);

            const resolved = await crateLicense(registry, name, version);
            if (!resolved) continue;

            const header = resolved.license && `Licensed under ${resolved.license}.`;
            entries.push({
                name,
                version,
                license: resolved.license,
                text: `${header}\n\n${resolved.text}`.trim(),
            });
        }
    }

    return entries;
}

/** What the config lists as shipping but arriving from neither npm, NuGet nor Cargo. */
async function extraEntries(extras) {
    const entries = [];

    for (const extra of extras) {
        const file = path.resolve(uiRoot, extra.file);
        if (!existsSync(file)) {
            console.warn(`no licence file for ${extra.name}, skipped`);
            continue;
        }

        const text = (await readFile(file, "utf8")).trim();
        entries.push({
            name: extra.name,
            version: extra.version ?? "",
            license: extra.license ?? detectLicense(text),
            text: `${extra.license ? `Licensed under ${extra.license}.` : ""}\n\n${text}`.trim(),
        });
    }

    return entries;
}

const config = existsSync(configPath) ? JSON.parse(await readFile(configPath, "utf8")) : {};

const entries = [
    ...(await npmEntries()),
    ...(await nugetEntries(await findProjectFile(config), {...DEFAULT_OVERRIDES, ...(config.licences ?? {})})),
    ...(await cargoEntries()),
    ...(await extraEntries(config.extras ?? [])),
].sort((a, b) => a.name.localeCompare(b.name, "en", {sensitivity: "base"}));

/** Emits a template literal that survives every character a licence text can contain. */
const literal = (value) => `\`${value.replace(/\\/g, "\\\\").replace(/`/g, "\\`").replace(/\$\{/g, "\\${")}\``;

const body = entries
    .map(
        (entry) =>
            `    {\n        name: ${JSON.stringify(entry.name)},\n        version: ${JSON.stringify(entry.version)},\n` +
            `        license: ${JSON.stringify(entry.license)},\n        text: ${literal(entry.text)},\n    },`
    )
    .join("\n");

await mkdir(path.dirname(outputPath), {recursive: true});

await writeFile(
    outputPath,
    `/**
 * Generated by \`npm run attributions\` — do not edit by hand.
 *
 * The open-source projects this app builds on, with the full licence text of each. npm packages are
 * read from their \`LICENSE\` file, NuGet packages declare an SPDX expression, and Rust crates are
 * listed from \`Cargo.lock\`; the canonical text of a declared expression lives in
 * \`scripts/licenses/\`.
 */
export interface Attribution {
    name: string;
    version: string;
    /** SPDX id, or a description when the package does not declare one. */
    license: string;
    /** The complete declaration shown when the entry is selected. */
    text: string;
}

export const ATTRIBUTIONS: Attribution[] = [
${body}
];
`,
    "utf8"
);

console.log(`Wrote ${entries.length} attributions to ${path.relative(uiRoot, outputPath)}`);
