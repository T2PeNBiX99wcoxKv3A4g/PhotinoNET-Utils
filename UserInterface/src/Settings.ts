import {Native} from "./Native";

/**
 * What every settings message answers with: the settings as the host keeps them, and the file they
 * live in. An app that adds anything of its own (bounds, capabilities) passes its own reply type.
 */
export interface SettingsReply<TSettings = unknown> {
    settings: TSettings;
    settingsFilePath: string;
}

/**
 * Loads the settings. The host has already applied the model's defaults and ranges, so the interface
 * has nothing to fill in or clamp by itself.
 */
export async function loadSettings<TReply = SettingsReply>(): Promise<TReply> {
    return await Native.request<TReply>("settings.get");
}

/** Stores the settings; the host clamps whatever is out of range and answers with what it kept. */
export async function saveSettings<TReply = SettingsReply>(settings: unknown): Promise<TReply> {
    return await Native.request<TReply>("settings.save", settings);
}

/** Puts every setting back to the host's defaults and answers with the settings now in use. */
export async function resetSettings<TReply = SettingsReply>(): Promise<TReply> {
    return await Native.request<TReply>("settings.reset");
}

let defaults: Promise<unknown> | null = null;

/**
 * The values a fresh install starts from, asked for once and remembered. A section's reset only has
 * to merge the keys it owns, so the interface needs no copy of any default at all.
 */
export function loadSettingDefaults<T = unknown>(): Promise<T> {
    // A failed request must not be remembered, or every later caller inherits the same rejection.
    defaults ??= Native.request<T>("settings.defaults").catch((error: unknown) => {
        defaults = null;
        throw error;
    });

    return defaults as Promise<T>;
}
