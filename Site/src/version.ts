import {useEffect, useState} from "react";
import {useSite} from "./context";

/** A plain `x.y.z` (or `x.y`); anything else is not a version worth showing. */
const PATTERN = /^\d+\.\d+/;

interface Release {
    tag_name?: string;
}

/**
 * The latest release tag, read from the GitHub API at runtime, so a new release shows up without
 * waiting for the site to be rebuilt. Null until it arrives, or when GitHub cannot be read.
 */
export function useVersion(): string | null {
    const site = useSite();
    const [version, setVersion] = useState<string | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        const endpoint = site.latestRelease ?? `${site.repository.replace(/\/$/, "")}/releases`.replace(
            "https://github.com", "https://api.github.com/repos") + "/latest";

        fetch(endpoint, {
            cache: "no-store",
            headers: {Accept: "application/vnd.github+json"},
            signal: controller.signal,
        })
            .then(async (response) => (response.ok ? ((await response.json()) as Release).tag_name ?? "" : ""))
            .then((tag) => {
                const value = tag.replace(/^v/, "").trim();
                if (PATTERN.test(value)) setVersion(value);
            })
            .catch(() => {
                // Offline or rate limited: the badge simply stays hidden.
            });

        return () => controller.abort();
    }, [site.latestRelease, site.repository]);

    return version;
}
