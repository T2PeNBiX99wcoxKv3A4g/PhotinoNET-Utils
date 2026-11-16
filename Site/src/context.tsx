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

/** The language picked last time, or the project's default when nothing (valid) is stored. */
function storedLanguage(options: SiteOptions): string {
    const value = window.localStorage.getItem(`${LANGUAGE_KEY}.${options.config.name}`);
    return options.languages.some((entry) => entry.id === value) ? value! : options.defaultLanguage;
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
        <SiteContext.Provider value={{site: options.config, docs: options.docs, scripts: options.scripts ?? [], languages: options.languages, language, setLanguage, copy}}>
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
