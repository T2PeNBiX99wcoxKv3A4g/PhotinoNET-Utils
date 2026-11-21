import React, {createContext, useContext, useEffect, useState} from "react";
import type {Docs, ScriptEntry, SiteConfig, SiteCopy, SiteLanguage, SiteOptions} from "./types";

const LANGUAGE_KEY = "photino-net-utils.site.language";

/** What the site knows once a project has handed it its data. */
interface SiteValue {
    site: SiteConfig;
    docs: Docs;
    scripts: ScriptEntry[];
    languages: SiteLanguage[];
    language: string;
    setLanguage: (language: string) => void;
    copy: SiteCopy;
}

const SiteContext = createContext<SiteValue | null>(null);

/** The language picked last time, or the browser's when nothing (valid) is stored. */
function storedLanguage(options: SiteOptions): string {
    const value = window.localStorage.getItem(`${LANGUAGE_KEY}.${options.config.name}`);
    if (options.languages.some((entry) => entry.id === value)) return value!;

    return browserLanguage(options) ?? options.defaultLanguage;
}

/**
 * The first language the browser asks for that the site offers. A regional tag the site does not
 * list ("ja-JP" when only "ja" is offered) falls back to the same base language.
 */
function browserLanguage(options: SiteOptions): string | null {
    const tags = navigator.languages?.length ? navigator.languages : [navigator.language];

    for (const tag of tags) {
        if (!tag) continue;

        const exact = options.languages.find((entry) => entry.id.toLowerCase() === tag.toLowerCase());
        if (exact) return exact.id;

        const base = tag.split("-")[0].toLowerCase();
        const partial = options.languages.find((entry) => entry.id.toLowerCase().split("-")[0] === base);
        if (partial) return partial.id;
    }

    return null;
}

/**
 * Holds the project's data for every page: the pages only read, so one provider around the shell is
 * enough. The project supplies the text in every language, which is why nothing here knows any.
 */
export const SiteProvider: React.FC<{ options: SiteOptions; children: React.ReactNode }> = ({options, children}) => {
    const [language, setLanguage] = useState(() => storedLanguage(options));

    useEffect(() => {
        window.localStorage.setItem(`${LANGUAGE_KEY}.${options.config.name}`, language);
        document.documentElement.lang = language;
    }, [language, options.config.name]);

    const copy = options.copy[language] ?? options.copy[options.defaultLanguage];

    return (
        <SiteContext.Provider value={{
            site: options.config,
            docs: options.docs,
            scripts: options.scripts ?? [],
            languages: options.languages,
            language,
            setLanguage,
            copy
        }}>
            {children}
        </SiteContext.Provider>
    );
};

function useSiteValue(): SiteValue {
    const value = useContext(SiteContext);
    if (!value) throw new Error("The site components must be rendered inside a SiteProvider.");

    return value;
}

export const useSite = (): SiteConfig => useSiteValue().site;
export const useCopy = (): SiteCopy => useSiteValue().copy;
export const useDocs = (): Docs => useSiteValue().docs;
export const useScripts = (): ScriptEntry[] => useSiteValue().scripts;
export const useLanguage = () => {
    const {language, setLanguage, languages} = useSiteValue();
    return {language, setLanguage, languages};
};
