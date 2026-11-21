export type LogLevel = "debug" | "info" | "warn" | "error";

/** Optional structured data; it is logged as JSON next to the message. */
export type LogContext = unknown;

export interface Logger {
    debug(message: string, context?: LogContext): void;

    info(message: string, context?: LogContext): void;

    warn(message: string, context?: LogContext): void;

    error(message: string, context?: LogContext): void;
}

function bridgeAvailable(): boolean {
    return typeof window !== "undefined" && typeof window.external?.sendMessage === "function";
}

function normalize(context: LogContext): LogContext {
    if (context instanceof Error) {
        return {
            name: context.name,
            message: context.message,
            stack: context.stack,
        };
    }

    return context;
}

/**
 * The transport shared by the public `Log` API and this library's own warnings. Keeping it
 * independent of `Native` means `Native` can report a malformed message without importing back
 * into itself.
 */
export function writeLog(level: LogLevel, message: string, context?: LogContext, source = "frontend"): void {
    // Without the desktop shell the browser console is the only place to see it.
    if (!bridgeAvailable()) {
        const consoleMethod = level === "warn" ? "warn" : level === "error" ? "error" : "log";
        if (context === undefined) console[consoleMethod](message);
        else console[consoleMethod](message, context);
        return;
    }

    window.external.sendMessage(JSON.stringify({
        type: "log.write",
        payload: {
            level,
            message,
            source,
            context: context === undefined ? undefined : normalize(context),
        },
    }));
}

function logger(source: string): Logger {
    return {
        debug: (message, context) => writeLog("debug", message, context, source),
        info: (message, context) => writeLog("info", message, context, source),
        warn: (message, context) => writeLog("warn", message, context, source),
        error: (message, context) => writeLog("error", message, context, source),
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
