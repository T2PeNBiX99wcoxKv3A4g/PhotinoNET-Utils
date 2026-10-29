import type {CSSProperties} from "react";
import {useEffect, useState} from "react";
import {Native} from "./Native";

/**
 * The window buttons a host that draws its own title bar needs: the state to render them with and
 * the messages that drive the native window.
 */
export interface WindowControls {
    /** Whether the window is maximized, as last reported by the host. */
    maximized: boolean;
    minimize: () => void;
    toggleMaximize: () => void;
    close: () => void;
}

const send = (type: string) => Native.send({type});

/** Window buttons: tracks `window.state` and sends the matching `window.*` message. */
export function useWindowControls(): WindowControls {
    const [maximized, setMaximized] = useState(false);

    useEffect(() => {
        if (!Native.available) return;

        return Native.on((message) => {
            if (message.type !== "window.state") return;

            const data = message.data as { maximized?: unknown };
            if (typeof data?.maximized === "boolean") setMaximized(data.maximized);
        });
    }, []);

    return {
        maximized,
        minimize: () => send("window.minimize"),
        toggleMaximize: () => send("window.toggleMaximize"),
        close: () => send("window.close"),
    };
}

/** Spread on a region the host moves the window from, the way a native title bar does. */
export function useWindowDrag() {
    return {
        onMouseDown: () => send("window.drag"),
        onDoubleClick: () => send("window.toggleMaximize"),
    };
}

/**
 * Whether the interface has to paint the window's rounded corners right now. The host reports that in
 * `app.info` (`rounded`), and it stops while the window is maximized: a maximized window covers the
 * screen, so its corners are the screen's.
 *
 * <p>
 * While it is true the page background is transparent (`html.window-rounded`), so the corners show
 * what is behind the window instead of a filled-in square.
 * </p>
 */
export function useWindowCorners(): boolean {
    const [rounded, setRounded] = useState(false);
    const {maximized} = useWindowControls();

    useEffect(() => {
        if (!Native.available) return;

        let cancelled = false;
        void Native.request<{ rounded?: boolean }>("app.info")
            .then((info) => {
                if (!cancelled) setRounded(info.rounded === true);
            })
            .catch(() => undefined);

        return () => {
            cancelled = true;
        };
    }, []);

    useEffect(() => {
        document.documentElement.classList.toggle("window-rounded", rounded);
    }, [rounded]);

    return rounded && !maximized;
}

type ResizeDirection = "top" | "bottom" | "left" | "right" | "topleft" | "topright" | "bottomleft" | "bottomright";

const grips: Array<[ResizeDirection, CSSProperties]> = [
    ["top", {top: 0, left: 8, right: 8, height: 5, cursor: "ns-resize"}],
    ["bottom", {bottom: 0, left: 8, right: 8, height: 5, cursor: "ns-resize"}],
    ["left", {top: 8, bottom: 8, left: 0, width: 5, cursor: "ew-resize"}],
    ["right", {top: 8, bottom: 8, right: 0, width: 5, cursor: "ew-resize"}],
    ["topleft", {top: 0, left: 0, width: 10, height: 10, cursor: "nwse-resize"}],
    ["topright", {top: 0, right: 0, width: 10, height: 10, cursor: "nesw-resize"}],
    ["bottomleft", {bottom: 0, left: 0, width: 10, height: 10, cursor: "nesw-resize"}],
    ["bottomright", {bottom: 0, right: 0, width: 10, height: 10, cursor: "nwse-resize"}],
];

/**
 * Invisible native resize grips. A chromeless window has no frame to grab, so the web layer asks
 * the host to start the standard resize loop from whichever edge was pressed.
 */
export function ResizeHandles() {
    return (
        <>
            {grips.map(([direction, style]) => (
                <div
                    key={direction}
                    onMouseDown={(event) => {
                        event.preventDefault();
                        Native.send({type: "window.resize", payload: {direction}});
                    }}
                    style={{position: "fixed", zIndex: 1000, ...style}}
                />
            ))}
        </>
    );
}
