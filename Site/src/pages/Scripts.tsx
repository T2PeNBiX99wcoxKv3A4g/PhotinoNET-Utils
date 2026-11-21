import React, {useState} from "react";
import styled from "styled-components";
import {Check, Copy, Download} from "lucide-react";
import {useCopy, useScripts} from "../context";
import {fill} from "../text";

const Header = styled.header`
    margin-bottom: 28px;
`;

const Title = styled.h1`
    margin: 0 0 8px;
    font-size: 1.9rem;
    letter-spacing: -0.02em;
`;

const Subtitle = styled.p`
    margin: 0;
    max-width: 640px;
    color: var(--text-secondary);
    font-size: 0.92rem;
`;

const Grid = styled.section`
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 16px;
`;

const Card = styled.article`
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 20px;
    border: 1px solid var(--border-color);
    border-radius: 12px;
    background: var(--bg-card);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
    transition: border-color 0.2s ease, box-shadow 0.2s ease, transform 0.2s ease, background 0.2s ease;

    &:hover {
        border-color: var(--border-color-hover);
        box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
        transform: translateY(-2px);
    }
`;

const Name = styled.h2`
    margin: 0;
    font-family: var(--font-mono);
    font-size: 0.9rem;
    font-weight: 600;
    overflow-wrap: anywhere;
`;

const Description = styled.p`
    flex: 1;
    margin: 0;
    color: var(--text-secondary);
    font-size: 0.85rem;
`;

const Source = styled.a`
    display: block;
    color: var(--text-muted);
    font-family: var(--font-mono);
    font-size: 0.72rem;
    text-decoration: none;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;

    &:hover {
        color: var(--accent);
    }
`;

const Actions = styled.div`
    display: flex;
    gap: 8px;
    margin-top: 4px;
`;

const Install = styled.a`
    flex: 1;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 7px;
    padding: 9px 14px;
    border-radius: 8px;
    border: 1px solid color-mix(in srgb, var(--accent-strong) 45%, transparent);
    background: color-mix(in srgb, var(--accent-strong) 18%, transparent);
    color: var(--text-primary);
    font-size: 0.85rem;
    font-weight: 600;
    text-align: center;
    text-decoration: none;
    transition: background 0.15s ease, transform 0.15s ease, box-shadow 0.15s ease;

    &:hover {
        background: color-mix(in srgb, var(--accent-strong) 30%, transparent);
        transform: translateY(-1px);
        box-shadow: 0 0 24px color-mix(in srgb, var(--accent-strong) 30%, transparent);
    }

    &:active {
        transform: translateY(0);
    }
`;

const CopyButton = styled.button`
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 9px 12px;
    border-radius: 8px;
    border: 1px solid var(--border-color);
    background: transparent;
    color: var(--text-secondary);
    font: inherit;
    font-size: 0.8rem;
    cursor: pointer;
    transition: color 0.15s ease, border-color 0.15s ease, background 0.15s ease;

    &:hover {
        color: var(--text-primary);
        border-color: var(--border-color-hover);
        background: color-mix(in srgb, var(--accent) 8%, transparent);
    }
`;

export const ScriptsPage: React.FC = () => {
    const scripts = useScripts();
    const copy = useCopy().scripts;
    const [copied, setCopied] = useState<string | null>(null);

    if (!copy) return null;

    const remember = (name: string, link: string) => {
        void navigator.clipboard.writeText(link).then(() => {
            setCopied(name);
            window.setTimeout(() => setCopied((current) => (current === name ? null : current)), 1500);
        });
    };

    return (
        <>
            <Header>
                <Title>{copy.title}</Title>
                <Subtitle>{fill(copy.subtitle, "{scheme}",
                    <code>{scripts[0]?.source.split("://")[0] ?? ""}://</code>)}</Subtitle>
            </Header>

            <Grid>
                {scripts.map((script) => (
                    <Card key={script.name}>
                        <Name>{script.name}</Name>
                        <Description>{script.description}</Description>
                        <Source href={script.source} title={script.source}>
                            {script.source}
                        </Source>
                        <Actions>
                            <Install href={script.install}>
                                <Download size={15}/>
                                {copy.install}
                            </Install>
                            <CopyButton onClick={() => remember(script.name, script.source)} type="button">
                                {copied === script.name ? <Check size={15}/> : <Copy size={15}/>}
                                {copied === script.name ? copy.copied : copy.copy}
                            </CopyButton>
                        </Actions>
                    </Card>
                ))}
            </Grid>
        </>
    );
};
