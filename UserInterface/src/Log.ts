import {Native} from "./Native";

export type LogLevel = "debug" | "info" | "warn" | "error";

/** Optional structured data; it is logged as JSON next to the message. */
export type LogContext = unknown;

export interface Logger {
    debug(message: string, context?: LogContext): void;

    info(message: string, context?: LogContext): void;

    warn(message: string, context?: LogContext): void;

    error(message: string, context?: LogContext): void;
}

function write(level: LogLevel, message: string, context?: LogContext, source = "frontend"): void {
    // Without the desktop shell the browser console is the only place to see it.
    if (!Native.available) {
        const consoleMethod = level === "warn" ? "warn" : level === "error" ? "error" : "log";
        if (context === undefined) console[consoleMethod](message);
        else console[consoleMethod](message, context);
        return;
    }

    Native.send({
        type: "log.write",
        payload: {
            level,
            message,
            source,
            context,
        },
    });
}

function logger(source: string): Logger {
    return {
        debug: (message, context) => write("debug", message, context, source),
        info: (message, context) => write("info", message, context, source),
        warn: (message, context) => write("warn", message, context, source),
        error: (message, context) => write("error", message, context, source),
    };
}

/**
 * Sends log lines to the host's Serilog pipeline. In a plain browser it falls back to the console.
 *
 * ```ts
 * Log.info("folder opened", {path});
 * const files = Log.child("file-list");
 * files.warn("thumbnail failed", {path});
 * ```
 */
export const Log: Logger & { child(source: string): Logger } = {
    ...logger("frontend"),
    child: logger,
};
