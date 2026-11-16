import React from "react";
import styled, {keyframes} from "styled-components";
import {BookOpen, Download, FileCode2, FileText, Lock, ShieldAlert, ShieldCheck, UserCog, UserPlus, Users, Zap, type LucideIcon} from "lucide-react";
import {siGithub} from "simple-icons";
import {useCopy, useSite} from "../context";
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

/** The icon drifts a little, which keeps the hero from feeling like a static screenshot. */
const float = keyframes`
    0%, 100% {
        transform: translateY(0);
    }
    50% {
        transform: translateY(-6px);
    }
`;

const Hero = styled.section`
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 12px;
    padding: 24px 0 8px;
    text-align: center;
    animation: ${fadeIn} 0.3s cubic-bezier(0.22, 1, 0.36, 1);
`;

const AppIcon = styled.img`
    width: 88px;
    height: 88px;
    filter: drop-shadow(0 12px 32px color-mix(in srgb, var(--accent-strong) 35%, transparent));
    animation: ${float} 6s ease-in-out infinite;
`;

const Title = styled.h1`
    margin: 0;
    font-size: 2.6rem;
    letter-spacing: -0.03em;
`;

const Tagline = styled.p`
    margin: 0;
    max-width: 560px;
    color: var(--text-secondary);
    font-size: 1.05rem;
`;

const Actions = styled.div`
    display: flex;
    flex-wrap: wrap;
    justify-content: center;
    gap: 10px;
    margin-top: 12px;
`;

const Button = styled.a<{ $primary?: boolean }>`
    display: inline-flex;
    align-items: center;
    gap: 8px;
    padding: 10px 18px;
    border-radius: 9px;
    border: 1px solid ${(props) => (props.$primary ? "color-mix(in srgb, var(--accent-strong) 50%, transparent)" : "var(--border-color)")};
    background: ${(props) => (props.$primary ? "color-mix(in srgb, var(--accent-strong) 20%, transparent)" : "transparent")};
    color: ${(props) => (props.$primary ? "var(--text-primary)" : "var(--text-secondary)")};
    font-size: 0.9rem;
    font-weight: 600;
    text-decoration: none;
    transition: background 160ms ease, border-color 160ms ease, color 160ms ease, transform 160ms ease, box-shadow 160ms ease;

    &:hover {
        background: ${(props) => (props.$primary ? "color-mix(in srgb, var(--accent-strong) 32%, transparent)" : "color-mix(in srgb, var(--accent) 8%, transparent)")};
        border-color: var(--border-color-hover);
        color: var(--text-primary);
        transform: translateY(-1px);
        box-shadow: ${(props) => (props.$primary ? "0 0 28px color-mix(in srgb, var(--accent-strong) 28%, transparent)" : "none")};
    }

    &:active {
        transform: translateY(0);
    }
`;

const Section = styled.section`
    margin-top: 56px;
    animation: ${fadeIn} 0.3s cubic-bezier(0.22, 1, 0.36, 1) both;
`;

const Heading = styled.h2`
    margin: 0 0 16px;
    font-size: 1.3rem;
    letter-spacing: -0.01em;
`;

const Lead = styled.p`
    margin: 0 0 18px;
    color: var(--text-secondary);
    font-size: 0.92rem;
`;

const Grid = styled.div`
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
    gap: 14px;
`;

const Feature = styled.article`
    padding: 18px;
    border: 1px solid var(--border-color);
    border-radius: 12px;
    background: var(--bg-card);
    transition: background 180ms ease, border-color 180ms ease, transform 180ms ease, box-shadow 180ms ease;

    &:hover {
        border-color: var(--border-color-hover);
        transform: translateY(-2px);
        box-shadow: 0 12px 32px rgba(0, 0, 0, 0.35);
    }
`;

const FeatureIcon = styled.span`
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 30px;
    height: 30px;
    margin-bottom: 10px;
    border-radius: 8px;
    background: color-mix(in srgb, var(--accent) 14%, transparent);
    color: var(--accent);
    transition: background 180ms ease, transform 180ms ease;

    ${Feature}:hover & {
        background: color-mix(in srgb, var(--accent) 24%, transparent);
        transform: scale(1.08);
    }
`;

const FeatureName = styled.h3`
    margin: 0 0 6px;
    font-size: 0.92rem;
`;

const FeatureText = styled.p`
    margin: 0;
    color: var(--text-secondary);
    font-size: 0.84rem;
`;

const Steps = styled.ol`
    display: grid;
    gap: 12px;
    margin: 0;
    padding: 0;
    list-style: none;
    counter-reset: step;
`;

const Step = styled.li`
    display: flex;
    gap: 12px;
    align-items: flex-start;
    padding: 14px 16px;
    border: 1px solid var(--border-color);
    border-radius: 12px;
    background: var(--bg-card);
    color: var(--text-body);
    font-size: 0.88rem;
    counter-increment: step;
    transition: background 180ms ease, border-color 180ms ease, transform 180ms ease;

    &::before {
        content: counter(step);
        display: inline-flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
        width: 22px;
        height: 22px;
        border-radius: 50%;
        background: color-mix(in srgb, var(--accent) 18%, transparent);
        color: var(--accent);
        font-family: var(--font-mono);
        font-size: 0.72rem;
        font-weight: 700;
    }

    &:hover {
        border-color: var(--border-color-hover);
        transform: translateX(2px);
    }
`;

const PlatformGrid = styled.div`
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
    gap: 12px;
`;

const Platform = styled.a`
    display: flex;
    flex-direction: column;
    gap: 4px;
    padding: 16px 18px;
    border: 1px solid var(--border-color);
    border-radius: 12px;
    background: var(--bg-card);
    text-decoration: none;
    transition: background 180ms ease, border-color 180ms ease, transform 180ms ease, box-shadow 180ms ease;

    &:hover {
        border-color: var(--border-color-hover);
        transform: translateY(-2px);
        box-shadow: 0 12px 32px rgba(0, 0, 0, 0.35);
    }
`;

const PlatformName = styled.span`
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text-primary);
    font-size: 0.95rem;
    font-weight: 600;
`;

const BrandIcon = styled.svg`
    width: 18px;
    height: 18px;
    flex-shrink: 0;
    fill: var(--accent);
`;

const PlatformNote = styled.span`
    color: var(--text-secondary);
    font-size: 0.8rem;
`;

/** The main window, full width under the hero. */
const Shot = styled.figure`
    margin: 40px 0 0;

    img {
        width: 100%;
        border: 1px solid var(--border-color);
        border-radius: 12px;
        box-shadow: 0 20px 50px rgba(0, 0, 0, 0.45);
    }

    figcaption {
        margin-top: 10px;
        color: var(--text-muted);
        font-size: 0.78rem;
    }
`;

/** A second picture beside its explanation on wide screens. */
const Popup = styled.section`
    display: grid;
    gap: 28px;
    align-items: center;

    @media (min-width: 880px) {
        grid-template-columns: 560px minmax(0, 1fr);
    }
`;

const PopupShot = styled.figure`
    margin: 0;
    max-width: 560px;

    img {
        width: 100%;
        border: 1px solid var(--border-color);
        border-radius: 12px;
        box-shadow: 0 20px 50px rgba(0, 0, 0, 0.45);
    }

    figcaption {
        margin-top: 10px;
        color: var(--text-muted);
        font-size: 0.78rem;
    }
`;

/** The detection limits sit at the bottom, away from the sales pitch. */
const Disclaimer = styled.aside`
    padding: 18px 20px;
    border: 1px solid rgba(251, 191, 36, 0.22);
    border-radius: 12px;
    background: rgba(251, 191, 36, 0.06);

    h2 {
        margin: 0 0 6px;
        color: #fbbf24;
        font-size: 0.95rem;
    }

    p {
        margin: 0;
        color: var(--text-secondary);
        font-size: 0.84rem;
    }
`;

/** The order the copy lists its features in; a project overrides it with its own glyphs. */
const defaultFeatureIcons: LucideIcon[] = [Users, ShieldAlert, ShieldCheck, Zap, UserCog, UserPlus, FileText, Lock];

/** simple-icons dropped the Windows mark, so the four squares are drawn here. */
const windowsLogo = {path: "M3 3h8v8H3zM13 3h8v8h-8zM3 13h8v8H3zM13 13h8v8h-8z"};

export const HomePage: React.FC = () => {
    const site = useSite();
    const home = useCopy().home;

    const featureIcons = site.icons?.features ?? defaultFeatureIcons;
    const platformIcons = site.icons?.platforms ?? [windowsLogo];

    return (
        <>
            <Hero>
                <AppIcon src={site.icon} alt=""/>
                <Title>{site.name}</Title>
                <Tagline>{home.tagline}</Tagline>
                <Actions>
                    <Button $primary href={site.releases ?? `${site.repository}/releases/latest`}>
                        <Download size={17}/>
                        {home.download}
                    </Button>
                    {site.docs.length > 0 && (
                        <Button href={docsHref(site.docs[0].slug)}>
                            <BookOpen size={17}/>
                            {home.documentation}
                        </Button>
                    )}
                    {site.scriptInstall && home.installScript && (
                        <Button href={site.scriptInstall}>
                            <FileCode2 size={17}/>
                            {home.installScript}
                        </Button>
                    )}
                    <Button href={site.repository}>
                        <BrandIcon viewBox="0 0 24 24" aria-hidden="true">
                            <path d={siGithub.path}/>
                        </BrandIcon>
                        GitHub
                    </Button>
                </Actions>
            </Hero>

            {site.screenshots?.main && (
                <Shot>
                    <img alt={home.mainWindowAlt ?? ""} src={site.screenshots.main}/>
                    {home.mainWindow && <figcaption>{home.mainWindow}</figcaption>}
                </Shot>
            )}

            <Section>
                <Heading>{home.whatTitle}</Heading>
                <Lead>{home.whatText}</Lead>
            </Section>

            {home.popupTitle && (
                <Section>
                    <Popup>
                        {site.screenshots?.popup && (
                            <PopupShot>
                                <img alt={home.popupAlt ?? ""} src={site.screenshots.popup}/>
                                {home.popupCaption && <figcaption>{home.popupCaption}</figcaption>}
                            </PopupShot>
                        )}
                        <div>
                            <Heading>{home.popupTitle}</Heading>
                            <Lead>{home.popupText}</Lead>
                        </div>
                    </Popup>
                </Section>
            )}

            {home.featuresTitle && home.features && (
                <Section>
                    <Heading>{home.featuresTitle}</Heading>
                    <Grid>
                        {home.features.map(({name, text}, index) => {
                            const Glyph = featureIcons[index] ?? ShieldCheck;

                            return (
                                <Feature key={name}>
                                    <FeatureIcon>
                                        <Glyph size={18}/>
                                    </FeatureIcon>
                                    <FeatureName>{name}</FeatureName>
                                    <FeatureText>{text}</FeatureText>
                                </Feature>
                            );
                        })}
                    </Grid>
                </Section>
            )}

            {home.howTitle && home.how && (
                <Section>
                    <Heading>{home.howTitle}</Heading>
                    <Steps>
                        {home.how.map((step) => <Step key={step}>{step}</Step>)}
                    </Steps>
                </Section>
            )}

            {home.installTitle && (
                <Section>
                    <Heading>{home.installTitle}</Heading>
                    <Lead>
                        {home.installBefore}
                        <a href={site.repository}>{home.readme}</a>
                        {home.installAfter}
                    </Lead>
                    <PlatformGrid>
                        {(home.platforms ?? []).map(({name, note}, index) => {
                            const icon = platformIcons[index] ?? windowsLogo;

                            return (
                                <Platform href={site.releases ?? `${site.repository}/releases/latest`} key={name}>
                                    <PlatformName>
                                        <BrandIcon viewBox="0 0 24 24" aria-hidden="true">
                                            <path d={icon.path}/>
                                        </BrandIcon>
                                        {name}
                                    </PlatformName>
                                    <PlatformNote>{note}</PlatformNote>
                                </Platform>
                            );
                        })}
                    </PlatformGrid>
                </Section>
            )}

            {home.noteTitle && (
                <Section>
                    <Disclaimer>
                        <h2>{home.noteTitle}</h2>
                        <p>{home.noteText}</p>
                    </Disclaimer>
                </Section>
            )}
        </>
    );
};
