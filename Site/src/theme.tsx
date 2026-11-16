import {createGlobalStyle} from "styled-components";
import type {SiteConfig} from "./types";

/**
 * The page's colours and type. Every value comes from the project's theme, so two sites built from
 * this package differ only in what their `site.config.mjs` says.
 */
export const GlobalStyle = createGlobalStyle<{ site: SiteConfig }>`
    :root {
        color-scheme: dark;
        --font-cjk: sans-serif;
        --font-sans: "Inter", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", Arial;
        --font-mono: "JetBrains Mono", ui-monospace, "Cascadia Mono", Consolas, "SFMono-Regular", Menlo, var(--font-cjk);

        --accent: ${(props) => props.site.theme.accent};
        --accent-strong: ${(props) => props.site.theme.accentStrong};
        --bg-primary: ${(props) => props.site.theme.background};
        --bg-card: ${(props) => props.site.theme.card};
        --bg-topbar: ${(props) => props.site.theme.topBar};
        --text-primary: ${(props) => props.site.theme.textPrimary};
        --text-secondary: ${(props) => props.site.theme.textSecondary};
        --text-muted: ${(props) => props.site.theme.textMuted};
        --text-body: ${(props) => props.site.theme.body};
        --border-color: ${(props) => props.site.theme.border};
        --border-color-hover: ${(props) => props.site.theme.borderHover};
    }

    /* The app picks the CJK face per language; the site does the same so the text matches. */
    html:lang(zh-TW) {
        --font-cjk: "Noto Sans TC", "Microsoft JhengHei UI", "PingFang TC";
    }

    html:lang(zh-CN) {
        --font-cjk: "Noto Sans SC", "Microsoft YaHei UI", "PingFang SC";
    }

    html:lang(ja) {
        --font-cjk: "Noto Sans JP", "Yu Gothic UI", "Hiragino Kaku Gothic ProN";
    }

    * {
        box-sizing: border-box;
    }

    body {
        margin: 0;
        min-height: 100vh;
        background-color: var(--bg-primary);
        background-image:
            radial-gradient(circle at 50% -10%, color-mix(in srgb, var(--accent-strong) 22%, transparent), transparent 60%),
            radial-gradient(circle at 85% 15%, color-mix(in srgb, var(--accent) 10%, transparent), transparent 55%);
        background-attachment: fixed;
        color: var(--text-primary);
        font-family: var(--font-sans), var(--font-cjk);
        line-height: 1.5;
        -webkit-font-smoothing: antialiased;
    }

    a {
        color: var(--accent);
    }

    code {
        font-family: var(--font-mono);
    }

    ::selection {
        background: color-mix(in srgb, var(--accent) 35%, transparent);
    }

    @media (prefers-reduced-motion: reduce) {
        *,
        *::before,
        *::after {
            animation-duration: 0.001ms !important;
            animation-iteration-count: 1 !important;
            transition-duration: 0.001ms !important;
        }
    }
`;
