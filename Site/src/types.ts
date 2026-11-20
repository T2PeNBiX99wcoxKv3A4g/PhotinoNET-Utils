import type {ComponentType} from "react";

/** One entry of the generated documentation, in one language. */
export interface DocEntry {
    slug: string;
    title: string;
    group: string;
    body: string;
    /** Where the page's latest text can be read at runtime, when the page names one. */
    remote?: string;
}

/** The documentation, by language; a missing language falls back to English. */
export type Docs = Record<string, DocEntry[]>;

/** A page the docs generator emits, and the group its sidebar entry sits under. */
export interface DocPageConfig {
    slug: string;
    group: string;
    /**
     * A markdown file to build the page from, relative to the site directory, such as
     * `../CHANGELOG.md`. A translation at `docs/<language>/<slug>.md` still wins over it.
     */
    source?: string;
    /**
     * Where the page is read from at runtime, so a site built before the file changed still shows
     * the newest text. A path inside the repository (`master/CHANGELOG.md`) or a full URL; the page
     * falls back to the generated body when the request fails. Only used while the page is not
     * translated, since a translation is what its readers should see.
     */
    remote?: string;
}

/** The colours a site paints with; the app's own tokens are the source of these values. */
export interface SiteTheme {
    accent: string;
    accentStrong: string;
    background: string;
    card: string;
    topBar: string;
    textPrimary: string;
    textSecondary: string;
    textMuted: string;
    body: string;
    border: string;
    borderHover: string;
}

/** A dusk of glyphs the pages draw, in the order the copy lists its entries. */
export interface SiteIcons {
    features?: ComponentType<{ size?: number }>[];
    platforms?: { path: string }[];
}

/** Everything a page needs that is not text: where the project lives and how it looks. */
export interface SiteConfig {
    /** The product name, as the header and the page titles write it. */
    name: string;
    /** The one-line description the page shares and the metadata repeats. */
    tagline: string;
    /** The icon shown next to the name, as a URL or a path relative to the site root. */
    icon: string;
    repository: string;
    /** Defaults to `<repository>/releases/latest`. */
    releases?: string;
    /** The GitHub API endpoint the version badge reads; the default follows the repository. */
    latestRelease?: string;
    theme: SiteTheme;
    docs: DocPageConfig[];
    icons?: SiteIcons;
    /** Pictures the home page shows, as URLs or paths under the site root. */
    screenshots?: {
        main?: string;
        popup?: string;
        /** Further pictures, shown as a grid after the introduction. */
        gallery?: {src: string; alt?: string}[];
    };
    /** The install link of a script a project offers, shown beside Download. */
    scriptInstall?: string;
    /** A project that names an inspiration shows it in the footer. */
    creditsUrl?: string;
    /** Where the WebView2 runtime can be installed; the home page links to it when set. */
    webView2Url?: string;
    /** Extra header entries beyond home, docs and GitHub, such as a scripts page. */
    extraNav?: { key: string; href: string; glyph?: "code" }[];
}

/** The text of one language, as `src/i18n/<language>.json` holds it. */
export interface SiteCopy {
    nav: { home: string; docs: string; [key: string]: string };
    version: string;
    footer: string;
    creditsBefore?: string;
    creditsLink?: string;
    creditsAfter?: string;
    home: {
        tagline: string;
        download: string;
        documentation: string;
        whatTitle: string;
        whatText: string;
        featuresTitle?: string;
        features?: { name: string; text: string }[];
        howTitle?: string;
        how?: string[];
        installTitle?: string;
        /** What Windows 10 needs, with {link} where the installer link goes. */
        installWindows?: string;
        /** What a Linux machine needs, in the same sentence. */
        installLinux?: string;
        readme?: string;
        platforms?: { name: string; note: string }[];
        /** A project with screenshots uses these instead of the "how" steps. */
        installScript?: string;
        mainWindow?: string;
        mainWindowAlt?: string;
        popupTitle?: string;
        popupText?: string;
        popupCaption?: string;
        popupAlt?: string;
        noteTitle?: string;
        noteText?: string;
    };
    docs: { [group: string]: string };
    scripts?: {
        title: string;
        subtitle: string;
        install: string;
        copy: string;
        copied: string;
    };
}

/** The language list the header offers. */
export interface SiteLanguage {
    id: string;
    label: string;
}

/** One installable script a project offers, as the scripts page lists it. */
export interface ScriptEntry {
    name: string;
    description: string;
    source: string;
    install: string;
}

/** What a project hands the shared site: its data, its text and its docs. */
export interface SiteOptions {
    config: SiteConfig;
    languages: SiteLanguage[];
    defaultLanguage: string;
    copy: Record<string, SiteCopy>;
    docs: Docs;
    scripts?: ScriptEntry[];
}
