import React, {createContext, useContext, useEffect, useState} from "react";
import {Native} from "./Native";

/** The string table the backend returned for one locale, plus every locale it knows. */
export interface I18nPayload {
    locale: string;
    strings: Record<string, string>;
    available: string[];
}

/**
 * Filled in by each app's generated key file through module augmentation:
 *
 * ```ts
 * declare module "@photino-net-utils/bridge" {
 *     interface LocalizationKeys {
 *         "tray.open": true;
 *     }
 * }
 * ```
 *
 * An app that has no generated file leaves it empty and every string is accepted.
 */
export interface LocalizationKeys {}

/** Every key the app ships, or `string` until the generated file augments the interface. */
export type LocalizationKey = keyof LocalizationKeys extends never ? string : keyof LocalizationKeys;

/** The translator; the key type comes from the augmented {@link LocalizationKeys}. */
export type Translate<TKey extends string = LocalizationKey> = (
    key: TKey,
    params?: Record<string, string | number>
) => string;

/** Falls back to the key itself so a missing translation is obvious but never breaks the UI. */
export function createTranslator<TKey extends string = LocalizationKey>(
    strings: Record<string, string>
): Translate<TKey> {
    // Keys are case-insensitive, exactly like PhotinoNetUtils.Localization: a translation file may
    // not declare the same key twice, and the backend keeps the first spelling when it does. A
    // lower-cased lookup table reproduces that rule without touching the payload.
    const lookup = new Map<string, string>();
    for (const [key, value] of Object.entries(strings)) {
        const normalized = key.toLowerCase();
        if (!lookup.has(normalized)) lookup.set(normalized, value);
    }

    return (key, params) => {
        const template = lookup.get(key.toLowerCase()) ?? key;
        if (!params) return template;

        return template.replace(/\{(\w+)}/g, (match, name: string) =>
            name in params ? String(params[name]) : match
        );
    };
}

/** Loads the string table for the current (or a specific) language from the backend. */
export async function loadStrings(locale?: string): Promise<I18nPayload> {
    if (!Native.available) return {locale: "en", strings: {}, available: ["en"]};

    return await Native.request<I18nPayload>("i18n.get", locale ? {locale} : undefined);
}

export const TranslationContext = createContext<Translate>(createTranslator({}));

/** BCP-47 tag of the language the backend resolved, for locale-aware formatting. */
export const LocaleContext = createContext<string>("en");

export function useTranslation<TKey extends string = LocalizationKey>(): Translate<TKey> {
    return useContext(TranslationContext) as Translate<TKey>;
}

export function useLocale(): string {
    return useContext(LocaleContext);
}

/** Loads the string table from the backend and reloads it whenever the settings change. */
export const LocalizationProvider: React.FC<{ children: React.ReactNode }> = ({children}) => {
    const [translate, setTranslate] = useState<Translate>(() => createTranslator({}));
    const [locale, setLocale] = useState("en");

    useEffect(() => {
        let cancelled = false;

        const load = async () => {
            try {
                const payload = await loadStrings();
                if (cancelled) return;

                setTranslate(() => createTranslator(payload.strings));
                setLocale(payload.locale);
            } catch {
                // Keep the key-fallback translator.
            }
        };

        void load();

        const unsubscribe = Native.on((message) => {
            if (message.type === "settings.changed") void load();
        });

        return () => {
            cancelled = true;
            unsubscribe();
        };
    }, []);

    /* Chromium picks the CJK fallback font from this, so it has to follow the UI language. */
    useEffect(() => {
        document.documentElement.lang = locale;
    }, [locale]);

    return (
        <TranslationContext.Provider value={translate}>
            <LocaleContext.Provider value={locale}>{children}</LocaleContext.Provider>
        </TranslationContext.Provider>
    );
};
