import React from "react";
import {createRoot} from "react-dom/client";
import {App} from "./App";
import {GlobalStyle} from "./theme";
import {SiteProvider} from "./context";
import type {SiteOptions} from "./types";

export type {
    DocEntry, DocPageConfig, Docs, ScriptEntry, SiteConfig, SiteCopy, SiteIcons, SiteLanguage, SiteOptions, SiteTheme,
} from "./types";

export {docsHref, homeHref, sectionHref} from "./text";

/**
 * Builds the site for one project and mounts it. Everything that changes between projects arrives in
 * `options`: the colours, the name and links, the text of every language, the documentation and the
 * scripts a project may offer.
 */
export function createSite(options: SiteOptions): React.FC {
    document.documentElement.lang = options.defaultLanguage;

    return function Site() {
        return (
            <React.StrictMode>
                <SiteProvider options={options}>
                    <GlobalStyle site={options.config}/>
                    <App/>
                </SiteProvider>
            </React.StrictMode>
        );
    };
}

/** Mounts the built site into `#root`. */
export function renderSite(options: SiteOptions) {
    const Site = createSite(options);
    createRoot(document.getElementById("root")!).render(<Site/>);
}
