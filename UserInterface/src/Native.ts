export interface NativeMessage {
    id?: unknown;
    type?: string;
    ok?: unknown;
    error?: unknown;
    data?: unknown;
    [key: string]: unknown;
}

declare global {
    interface External {
        sendMessage(message: string): void;

        receiveMessage(callback: (message: string) => void): void;
    }
}

type NativeMessageHandler = (message: NativeMessage) => void;

interface PendingRequest {
    resolve: (value: unknown) => void;
    reject: (reason: Error) => void;
    timeoutId: ReturnType<typeof setTimeout>;
}

const DEFAULT_TIMEOUT_MS = 15_000;

const handlers = new Set<NativeMessageHandler>();
const pendingRequests = new Map<string, PendingRequest>();

let initialized = false;
let requestSequence = 0;

function hasBridge(): boolean {
    return typeof window !== "undefined" && typeof window.external?.sendMessage === "function";
}

/**
 * Registers the single `receiveMessage` callback used for every message coming from C#.
 * Photino's bridge adds a listener each time it is called, so this must run exactly once.
 */
function ensureInitialized(): void {
    if (initialized) return;
    initialized = true;

    if (typeof window === "undefined" || typeof window.external?.receiveMessage !== "function") {
        return;
    }

    window.external.receiveMessage((rawMessage) => handleIncoming(rawMessage));
}

function handleIncoming(rawMessage: string): void {
    let message: NativeMessage;
    try {
        message = JSON.parse(rawMessage);
    } catch (error) {
        console.warn("[Native] Received a non-JSON message:", rawMessage, error);
        return;
    }

    // Resolve a pending request/response pair when the reply carries a matching id.
    const id = typeof message.id === "string" ? message.id : undefined;
    const pending = id ? pendingRequests.get(id) : undefined;
    if (id && pending) {
        pendingRequests.delete(id);
        clearTimeout(pending.timeoutId);

        if (message.ok === false) {
            pending.reject(new Error(typeof message.error === "string" ? message.error : "Native request failed."));
        } else {
            pending.resolve(message.data);
        }
    }

    // Always forward the raw message to any subscribed listeners (events have no id).
    handlers.forEach((handler) => {
        try {
            handler(message);
        } catch (error) {
            console.error("[Native] Message handler threw:", error);
        }
    });
}

export const Native = {
    /** True when running inside the Photino desktop shell (false in a plain browser). */
    get available(): boolean {
        return hasBridge();
    },

    /** Fire-and-forget message to C#. */
    send(message: Record<string, unknown> & { type: string }): void {
        if (!hasBridge()) {
            console.debug("[Native] Bridge unavailable, dropping message:", message);
            return;
        }

        ensureInitialized();
        window.external.sendMessage(JSON.stringify(message));
    },

    /** Sends a message and resolves with the C# reply's `data` field. */
    request<T = unknown>(type: string, payload?: unknown, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<T> {
        if (!hasBridge()) {
            return Promise.reject(new Error("Native bridge is not available."));
        }

        ensureInitialized();

        const id = `req-${++requestSequence}-${Date.now()}`;
        return new Promise<T>((resolve, reject) => {
            const timeoutId = setTimeout(() => {
                pendingRequests.delete(id);
                reject(new Error(`Native request '${type}' timed out after ${timeoutMs}ms.`));
            }, timeoutMs);

            pendingRequests.set(id, {resolve: resolve as (value: unknown) => void, reject, timeoutId});
            window.external.sendMessage(JSON.stringify({type, id, payload}));
        });
    },

    /** Subscribes to every message from C# (replies and unsolicited events). Returns an unsubscribe fn. */
    on(handler: NativeMessageHandler): () => void {
        ensureInitialized();
        handlers.add(handler);
        return () => handlers.delete(handler);
    },
};
