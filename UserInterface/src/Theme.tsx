import {useEffect, useState} from "react";

/** Stored theme: follow the operating system, or pin one. */
export type ThemeMode = "system" | "light" | "dark";

export type ResolvedTheme = "light" | "dark";

const QUERY = "(prefers-color-scheme: dark)";

/** Resolves a stored mode against the OS preference. */
export function resolveTheme(mode: ThemeMode, systemDark: boolean): ResolvedTheme {
    return mode === "system" ? (systemDark ? "dark" : "light") : mode;
}

/** True when the OS currently asks for a dark interface. */
export function systemPrefersDark(): boolean {
    return typeof window !== "undefined" && window.matchMedia(QUERY).matches;
}

/**
 * Applies the theme to `<html data-theme>` and follows the OS while `mode` is `system`.
 * The resolved value is returned for components that need to branch on it.
 */
export function useTheme(mode: ThemeMode): ResolvedTheme {
    const [systemDark, setSystemDark] = useState(systemPrefersDark);

    useEffect(() => {
        const media = window.matchMedia(QUERY);
        const update = () => setSystemDark(media.matches);

        update();
        media.addEventListener("change", update);
        return () => media.removeEventListener("change", update);
    }, []);

    const resolved = resolveTheme(mode, systemDark);

    useEffect(() => {
        document.documentElement.dataset.theme = resolved;
    }, [resolved]);

    return resolved;
}
