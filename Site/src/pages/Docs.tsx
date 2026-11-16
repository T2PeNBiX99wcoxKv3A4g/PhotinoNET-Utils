import React from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import styled, {keyframes} from "styled-components";
import {useCopy, useDocs, useLanguage, useSite} from "../context";
import {docsHref} from "../text";

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

const Layout = styled.div`
    display: flex;
    gap: 32px;
    align-items: flex-start;
    animation: ${fadeIn} 0.24s cubic-bezier(0.22, 1, 0.36, 1);

    @media (max-width: 760px) {
        flex-direction: column;
        gap: 20px;
    }
`;

const Sidebar = styled.aside`
    position: sticky;
    top: 24px;
    width: 210px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 18px;

    @media (max-width: 760px) {
        position: static;
        width: 100%;
    }
`;

const Group = styled.div`
    display: flex;
    flex-direction: column;
    gap: 2px;
`;

const GroupTitle = styled.span`
    padding: 0 10px 6px;
    color: var(--text-muted);
    font-size: 0.7rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
`;

const SideLink = styled.a<{ $active: boolean }>`
    padding: 7px 10px;
    border-radius: 7px;
    color: ${(props) => (props.$active ? "var(--text-primary)" : "var(--text-secondary)")};
    background: ${(props) => (props.$active ? "color-mix(in srgb, var(--accent) 14%, transparent)" : "transparent")};
    font-size: 0.85rem;
    text-decoration: none;
    transition: background 120ms ease, color 120ms ease;

    &:hover {
        color: var(--text-primary);
        background: color-mix(in srgb, var(--accent) 8%, transparent);
    }
`;

const Article = styled.article`
    flex: 1;
    min-width: 0;
    padding: 28px 32px;
    border: 1px solid var(--border-color);
    border-radius: 16px;
    background: var(--bg-card);
    box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
`;

const Body = styled.div`
    font-size: 0.92rem;
    color: var(--text-body);
    line-height: 1.65;

    h1 {
        margin: 0 0 16px;
        font-size: 1.8rem;
        color: var(--text-primary);
        letter-spacing: -0.02em;
    }

    h2 {
        margin: 32px 0 12px;
        padding-bottom: 8px;
        border-bottom: 1px solid var(--border-color);
        font-size: 1.25rem;
        color: var(--text-primary);
    }

    h3 {
        margin: 24px 0 8px;
        font-size: 1.02rem;
        color: var(--text-primary);
    }

    p {
        margin: 0 0 12px;
    }

    ul,
    ol {
        margin: 0 0 12px;
        padding-left: 22px;
    }

    li {
        margin: 4px 0;
    }

    a {
        color: var(--accent);
        text-decoration: none;
    }

    a:hover {
        text-decoration: underline;
    }

    code {
        padding: 1px 5px;
        border-radius: 4px;
        background: color-mix(in srgb, var(--accent) 12%, transparent);
        color: var(--accent);
        font-family: var(--font-mono);
        font-size: 0.85em;
    }

    pre {
        margin: 0 0 16px;
        padding: 14px 16px;
        border: 1px solid var(--border-color);
        border-radius: 10px;
        background: color-mix(in srgb, var(--bg-primary) 85%, black);
        overflow-x: auto;
    }

    pre code {
        padding: 0;
        background: none;
        color: var(--text-body);
        font-size: 0.82rem;
    }

    table {
        width: 100%;
        margin: 0 0 16px;
        border-collapse: collapse;
        font-size: 0.85rem;
    }

    th,
    td {
        padding: 8px 10px;
        border-bottom: 1px solid var(--border-color);
        text-align: left;
        vertical-align: top;
    }

    th {
        color: var(--text-secondary);
        font-weight: 600;
    }

    blockquote {
        margin: 0 0 16px;
        padding: 8px 14px;
        border-left: 3px solid color-mix(in srgb, var(--accent-strong) 50%, transparent);
        background: color-mix(in srgb, var(--accent-strong) 8%, transparent);
        color: var(--text-secondary);
    }

    hr {
        margin: 24px 0;
        border: 0;
        border-top: 1px solid var(--border-color);
    }
`;

interface DocsPageProps {
    page: string;
}

export const DocsPage: React.FC<DocsPageProps> = ({page}) => {
    const site = useSite();
    const docs = useDocs();
    const {language} = useLanguage();
    const copy = useCopy();

    // A language without its own pages falls back to the English ones the generator always emits.
    const pages = docs[language] ?? docs.en;
    const current = pages.find((doc) => doc.slug === page) ?? pages[0];
    const groups = [...new Set(site.docs.map((entry) => entry.group))];

    if (!current) return null;

    return (
        <Layout>
            <Sidebar>
                {groups.map((group) => (
                    <Group key={group}>
                        <GroupTitle>{copy.docs[group] ?? group}</GroupTitle>
                        {pages
                            .filter((doc) => doc.group === group)
                            .map((doc) => (
                                <SideLink
                                    $active={doc.slug === current.slug}
                                    href={docsHref(doc.slug)}
                                    key={doc.slug}
                                >
                                    {doc.title}
                                </SideLink>
                            ))}
                    </Group>
                ))}
            </Sidebar>

            <Article>
                <Body>
                    <Markdown remarkPlugins={[remarkGfm]}>{current.body}</Markdown>
                </Body>
            </Article>
        </Layout>
    );
};
