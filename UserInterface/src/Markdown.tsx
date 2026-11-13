import DOMPurify from "dompurify";
import {marked} from "marked";
import {useMemo} from "react";

export interface MarkdownProps {
    /** GitHub-flavoured Markdown, such as a release changelog. */
    source: string;
    className?: string;
}

/**
 * Renders Markdown as HTML. The source usually comes from a release body rather than from the app,
 * so the result is sanitized before it reaches the DOM. The caller's class styles the elements:
 *
 * ```css
 * .changelog h2 { ... }
 * .changelog ul { ... }
 * ```
 */
export function Markdown({source, className}: MarkdownProps) {
    const html = useMemo(
        () => DOMPurify.sanitize(marked.parse(source, {async: false, gfm: true}) as string),
        [source]
    );

    return <div className={className} dangerouslySetInnerHTML={{__html: html}}/>;
}
