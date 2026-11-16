import React, {useEffect, useState} from "react";
import styled, {keyframes} from "styled-components";
import {BookOpen, FileCode2, Globe, House} from "lucide-react";
import {siGithub} from "simple-icons";
import {useCopy, useDocs, useLanguage, useSite} from "./context";
import {docsHref, homeHref, sectionHref} from "./text";
import {useVersion} from "./version";
import {HomePage} from "./pages/Home";
import {DocsPage} from "./pages/Docs";
import {ScriptsPage} from "./pages/Scripts";

/** The app fades its panels in the same way, so the site uses the same curve. */
const fadeIn = keyframes`
    from {
        opacity: 0;
        transform: translateY(6px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
`;

const Shell = styled.div`
    display: flex;
    flex-direction: column;
    min-height: 100vh;
`;

const TopBar = styled.header`
    position: sticky;
    top: 0;
    z-index: 10;
    display: flex;
    align-items: center;
    gap: 24px;
    padding: 14px 24px;
    border-bottom: 1px solid var(--border-color);
    background: var(--bg-topbar);
    backdrop-filter: blur(16px);
    animation: ${fadeIn} 0.24s cubic-bezier(0.22, 1, 0.36, 1);
`;

const Brand = styled.a`
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text-primary);
    font-size: 1.05rem;
    font-weight: 700;
    letter-spacing: -0.02em;
    text-decoration: none;
`;

const BrandIcon = styled.img`
    width: 22px;
    height: 22px;
    transition: transform 0.2s cubic-bezier(0.22, 1, 0.36, 1);

    ${Brand}:hover & {
        transform: rotate(-6deg) scale(1.06);
    }
`;

/** The brand and its release badge travel together; the nav keeps the rest of the row. */
const BrandGroup = styled.div`
    display: flex;
    align-items: center;
    gap: 10px;
`;

const VersionBadge = styled.a`
    padding: 2px 8px;
    border: 1px solid var(--border-color);
    border-radius: 999px;
    color: var(--text-secondary);
    font-size: 0.68rem;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
    text-decoration: none;
    transition: background 140ms ease, border-color 140ms ease, color 140ms ease;

    &:hover {
        color: var(--text-primary);
        border-color: var(--border-color-hover);
        background: color-mix(in srgb, var(--accent) 8%, transparent);
    }
`;

const Nav = styled.nav`
    display: flex;
    align-items: center;
    gap: 4px;
    margin-left: auto;
`;

const NavLink = styled.a<{ $active?: boolean }>`
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 7px;
    color: ${(props) => (props.$active ? "var(--text-primary)" : "var(--text-secondary)")};
    background: ${(props) => (props.$active ? "color-mix(in srgb, var(--accent) 16%, transparent)" : "transparent")};
    font-size: 0.85rem;
    text-decoration: none;
    transition: background 120ms ease, color 120ms ease;

    &:hover {
        color: var(--text-primary);
        background: color-mix(in srgb, var(--accent) 8%, transparent);
    }
`;

/** Keeps a brand mark (simple-icons has no React wrapper) in step with the lucide glyphs beside it. */
const BrandMark = styled.svg`
    width: 15px;
    height: 15px;
    flex-shrink: 0;
    fill: currentColor;
`;

const LanguagePicker = styled.label`
    position: relative;
    display: inline-flex;
    align-items: center;
    margin-left: 4px;

    svg {
        position: absolute;
        left: 8px;
        color: var(--text-secondary);
        pointer-events: none;
    }
`;

const LanguageSelect = styled.select`
    padding: 6px 8px 6px 28px;
    border-radius: 7px;
    border: 1px solid var(--border-color);
    background: var(--bg-card);
    color: var(--text-secondary);
    font: inherit;
    font-size: 0.8rem;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease, color 120ms ease;

    &:hover {
        color: var(--text-primary);
        border-color: var(--border-color-hover);
    }
`;

const Content = styled.main`
    flex: 1;
    width: 100%;
    max-width: 1080px;
    margin: 0 auto;
    padding: 40px 24px 64px;
    animation: ${fadeIn} 0.24s cubic-bezier(0.22, 1, 0.36, 1);
`;

const Footer = styled.footer`
    padding: 20px 24px 28px;
    color: var(--text-muted);
    font-size: 0.78rem;
    text-align: center;

    p {
        margin: 6px 0 0;
    }
`;

/** `#/docs` and `#%2Fdocs` both mean the same page; a stray encoding must not break the route. */
function decodeHash(value: string) {
    try {
        return decodeURIComponent(value);
    } catch {
        return value;
    }
}

/** `#/section/page` — the site is static, so the hash keeps deep links working on Pages. */
function useRoute() {
    const [hash, setHash] = useState(() => window.location.hash);

    useEffect(() => {
        const update = () => setHash(window.location.hash);
        window.addEventListener("hashchange", update);
        return () => window.removeEventListener("hashchange", update);
    }, []);

    const path = decodeHash(hash.replace(/^#\/?/, "")).replace(/^\/+/, "");
    const [section, ...rest] = path.split("/");

    return {section: section || "home", page: rest.join("/")};
}

/** Keeps the browser tab in step with the current page and language. */
function useTitle(section: string, page: string, language: string, name: string) {
    const docs = useDocs();
    const copy = useCopy();

    useEffect(() => {
        const entry = (docs[language] ?? docs.en).find((doc) => doc.slug === page);
        const title = section === "docs" ? entry?.title ?? copy.nav.docs : "";

        document.title = title ? `${title} — ${name}` : name;
    }, [section, page, language, name, docs, copy.nav.docs]);
}

const Site: React.FC = () => {
    const site = useSite();
    const route = useRoute();
    const {language, setLanguage, languages} = useLanguage();
    const copy = useCopy();
    const version = useVersion();

    useTitle(route.section, route.page, language, site.name);

    const extra = site.extraNav?.find((entry) => entry.key === route.section);

    return (
        <Shell>
            <TopBar>
                <BrandGroup>
                    <Brand href={homeHref()}>
                        <BrandIcon src={site.icon} alt=""/>
                        {site.name}
                    </Brand>
                    {version && (
                        <VersionBadge
                            href={site.releases ?? `${site.repository}/releases/latest`}
                            aria-label={`${copy.version} ${version}`}
                            title={`${copy.version} ${version}`}
                        >
                            v{version}
                        </VersionBadge>
                    )}
                </BrandGroup>
                <Nav>
                    <NavLink $active={route.section === "home"} href={homeHref()}>
                        <House size={15}/>
                        {copy.nav.home}
                    </NavLink>
                    {site.docs.length > 0 && (
                        <NavLink $active={route.section === "docs"} href={docsHref(site.docs[0].slug)}>
                            <BookOpen size={15}/>
                            {copy.nav.docs}
                        </NavLink>
                    )}
                    {site.extraNav?.map((entry) => (
                        <NavLink $active={route.section === entry.key} href={sectionHref(entry.key)} key={entry.key}>
                            <FileCode2 size={15}/>
                            {copy.nav[entry.key] ?? entry.key}
                        </NavLink>
                    ))}
                    <NavLink href={site.repository}>
                        <BrandMark viewBox="0 0 24 24" aria-hidden="true">
                            <path d={siGithub.path}/>
                        </BrandMark>
                        GitHub
                    </NavLink>
                    {languages.length > 1 && (
                        <LanguagePicker>
                            <Globe size={14}/>
                            <LanguageSelect
                                aria-label="Language"
                                value={language}
                                onChange={(event) => setLanguage(event.target.value)}
                            >
                                {languages.map((entry) => (
                                    <option value={entry.id} key={entry.id}>
                                        {entry.label}
                                    </option>
                                ))}
                            </LanguageSelect>
                        </LanguagePicker>
                    )}
                </Nav>
            </TopBar>

            <Content key={`${route.section}/${route.page}/${language}`}>
                {route.section === "docs"
                    ? <DocsPage page={route.page}/>
                    : extra
                        ? <ScriptsPage/>
                        : <HomePage/>}
            </Content>

            <Footer>
                {copy.footer}
                {site.creditsUrl && copy.creditsBefore && (
                    <p>
                        {copy.creditsBefore}
                        <a href={site.creditsUrl} rel="noreferrer" target="_blank">{copy.creditsLink}</a>
                        {copy.creditsAfter}
                    </p>
                )}
            </Footer>
        </Shell>
    );
};

export const App: React.FC = () => <Site/>;
