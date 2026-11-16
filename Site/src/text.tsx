import React from "react";

/**
 * Navigation inside the site stays in the SPA. The generated static pages exist for search engines;
 * opening one from a result is a normal entry point, but moving between docs should not pay for a
 * full page load each time.
 */
export const docsHref = (slug: string) => `#/docs/${slug}`;

export const homeHref = () => "#/";

export const sectionHref = (section: string) => `#/${section}`;

/**
 * Puts `node` where `placeholder` appears in `text`. Used for the few strings that carry a link or a
 * code element, so translators only see plain text.
 */
export function fill(text: string, placeholder: string, node: React.ReactNode): React.ReactNode {
    const [before, ...rest] = text.split(placeholder);
    if (rest.length === 0) return text;

    return (
        <>
            {before}
            {node}
            {rest.join(placeholder)}
        </>
    );
}
