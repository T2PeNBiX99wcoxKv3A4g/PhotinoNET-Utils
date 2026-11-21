import {Native} from "./Native";

/**
 * The binary table block the local server sends. The page copies the bytes once and reads the
 * numbers in place; only the strings it actually draws are decoded.
 *
 * Layout, little-endian:
 *
 *     header  "PNUB" u16 version u16 kind u32 rows u32 textCols u32 numberCols
 *             u32 metaCount u32 poolBytes u32 reserved
 *     names   u16 length + UTF-8, every text column then every number column
 *     meta    u16 key length + key, u16 value length + value
 *     rows    textCols × (u32 offset, u16 length), numberCols × f64
 *     pool    the UTF-8 strings the row offsets point at
 */

/** "PNUB" */
const DataMagic = 0x42554e50;
const DataVersion = 1;
const DataHeaderSize = 32;

const Utf8 = new TextDecoder();

export interface DataBlock {
    kind: number;
    meta: Record<string, string>;
    columns: {
        text: string[];
        number: string[];
    };
    /** One object per row: text columns as strings, number columns as numbers. */
    rows: Record<string, string | number>[];
    /** The row count on the wire, which is what a page returns. */
    count: number;
    /** The whole list's row count when the server sends `X-Data-Total`; the page count otherwise. */
    total: number;
}

export interface DataClientOptions {
    /** The bridge message that returns the data plane's base URL, for example `app.data`. */
    baseMessage: string;
    /** The field of that reply holding the base URL. Defaults to `base`. */
    baseField?: string;
    /** Extra headers for every data request. */
    headers?: HeadersInit;
    /** Fetch cache mode. Defaults to `no-cache`, so a refreshed list is never a cached one. */
    cache?: RequestCache;
}

/**
 * A client for the local server's data plane: binary blocks for lists and raw bytes for images and
 * other files. The base URL is asked once through the bridge and kept for the life of the page.
 */
export class DataClient {
    private baseTask: Promise<string> | null = null;
    private readonly options: Required<Pick<DataClientOptions, "baseMessage">> & DataClientOptions;

    constructor(options: DataClientOptions) {
        this.options = options;
    }

    /** The local server's prefix, or an empty string when the host is not available. */
    base(): Promise<string> {
        if (!Native.available) return Promise.resolve("");

        this.baseTask ??= Native.request<Record<string, unknown> | null>(this.options.baseMessage).then(
            (payload) => {
                const value = payload?.[this.options.baseField ?? "base"];
                return typeof value === "string" ? value : "";
            },
            () => {
                // Ask again next time instead of remembering a failure forever.
                this.baseTask = null;
                return "";
            },
        );

        return this.baseTask;
    }

    /** Forgets the cached base URL, so the next call asks the bridge again. */
    reset(): void {
        this.baseTask = null;
    }

    /** Fetches and decodes one block. */
    async readBlock(path: string, init?: RequestInit): Promise<DataBlock> {
        const base = await this.base();
        if (!base) throw new Error("The data server is not running.");

        const response = await fetch(this.join(base, path), this.request(init));
        if (!response.ok) throw new Error(`The data request failed (${response.status}).`);

        const block = decodeDataBlock(await response.arrayBuffer());
        const total = Number(response.headers.get("x-data-total") ?? "");

        if (Number.isFinite(total) && total >= block.count) block.total = total;

        return block;
    }

    /** Fetches raw bytes, for an image or another file the caller decodes itself. */
    async bytes(path: string, init?: RequestInit): Promise<Uint8Array> {
        const response = await this.response(path, init);
        return new Uint8Array(await response.arrayBuffer());
    }

    /** A loopback URL for an image or file. The web view fetches it with its own cache. */
    async url(path: string): Promise<string> {
        const base = await this.base();
        if (!base) throw new Error("The data server is not running.");
        return this.join(base, path);
    }

    /** Alias of {@link url} for the common image case. */
    image(path: string): Promise<string> {
        return this.url(path);
    }

    private async response(path: string, init?: RequestInit): Promise<Response> {
        const base = await this.base();
        if (!base) throw new Error("The data server is not running.");

        const response = await fetch(this.join(base, path), this.request(init));
        if (!response.ok) throw new Error(`The data request failed (${response.status}).`);
        return response;
    }

    private request(init?: RequestInit): RequestInit {
        return {
            cache: this.options.cache ?? "no-cache",
            headers: this.options.headers,
            ...init,
        };
    }

    private join(base: string, path: string): string {
        return `${base.replace(/\/+$/, "")}/${path.replace(/^\/+/, "")}`;
    }
}

/** Reads the block itself, for a caller that already has the bytes. */
export function decodeDataBlock(buffer: ArrayBuffer): DataBlock {
    const view = new DataView(buffer);

    if (buffer.byteLength < DataHeaderSize || view.getUint32(0, true) !== DataMagic)
        throw new Error("Not a data block.");

    if (view.getUint16(4, true) !== DataVersion)
        throw new Error("This build cannot read that data block.");

    const kind = view.getUint16(6, true);
    const count = view.getUint32(8, true);
    const textColumns = view.getUint32(12, true);
    const numberColumns = view.getUint32(16, true);
    const metaCount = view.getUint32(20, true);
    const poolBytes = view.getUint32(24, true);

    let at = DataHeaderSize;

    const read = () => {
        const length = view.getUint16(at, true);
        at += 2;
        const value = Utf8.decode(new Uint8Array(buffer, at, length));
        at += length;
        return value;
    };

    const names: string[] = [];
    for (let index = 0; index < textColumns + numberColumns; index++) names.push(read());

    const meta: Record<string, string> = {};
    for (let index = 0; index < metaCount; index++) {
        const key = read();
        meta[key] = read();
    }

    const pool = new Uint8Array(buffer, buffer.byteLength - poolBytes, poolBytes);
    const string = (offset: number, length: number) =>
        Utf8.decode(pool.subarray(offset, offset + length));

    const rows = new Array<Record<string, string | number>>(count);

    for (let row = 0; row < count; row++) {
        const values: Record<string, string | number> = {};

        for (let column = 0; column < textColumns; column++) {
            values[names[column]] = string(view.getUint32(at, true), view.getUint16(at + 4, true));
            at += 6;
        }

        for (let column = 0; column < numberColumns; column++) {
            values[names[textColumns + column]] = view.getFloat64(at, true);
            at += 8;
        }

        rows[row] = values;
    }

    return {
        kind,
        meta,
        columns: {
            text: names.slice(0, textColumns),
            number: names.slice(textColumns),
        },
        rows,
        count,
        total: count,
    };
}

/** Convenience for one call, without keeping a client around. */
export async function readDataBlock(base: string, path: string, init?: RequestInit): Promise<DataBlock> {
    const response = await fetch(`${base.replace(/\/+$/, "")}/${path.replace(/^\/+/, "")}`, {
        cache: "no-cache",
        ...init,
    });

    if (!response.ok) throw new Error(`The data request failed (${response.status}).`);

    const block = decodeDataBlock(await response.arrayBuffer());
    const total = Number(response.headers.get("x-data-total") ?? "");

    if (Number.isFinite(total) && total >= block.count) block.total = total;

    return block;
}

/** Convenience for raw bytes, without keeping a client around. */
export async function fetchDataBytes(base: string, path: string, init?: RequestInit): Promise<Uint8Array> {
    const response = await fetch(`${base.replace(/\/+$/, "")}/${path.replace(/^\/+/, "")}`, {
        cache: "no-cache",
        ...init,
    });

    if (!response.ok) throw new Error(`The data request failed (${response.status}).`);
    return new Uint8Array(await response.arrayBuffer());
}
