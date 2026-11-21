namespace PhotinoNetUtils.Data;

/// <summary>
///     One writer per database file, shared by every <see cref="SqliteDatabase" /> instance that
///     names the same file. Different files get different gates, so two databases can be written at
///     the same time. Readers never take a gate; SQLite's own file locks and the busy timeout keep
///     them safe across processes, and this gate keeps the same process from making itself wait.
/// </summary>
/// <remarks>
///     The gate is reference counted: it disappears when no operation is using or waiting for it,
///     so browsing thousands of folders does not leave thousands of semaphores behind.
/// </remarks>
internal static class SqliteFileGate
{
    private static readonly Dictionary<string, Entry> Entries =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly Lock Sync = new();

    public static async Task<Lease> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        var key = Path.GetFullPath(path);
        Entry entry;

        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                entry = new();
                Entries[key] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, false);
            throw;
        }

        return new(key, entry);
    }

    private static void Release(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

        lock (Sync)
            if (--entry.References == 0)
                Entries.Remove(key);
    }

    internal sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int References;
    }

    internal sealed class Lease(string key, Entry entry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Release(key, entry, true);
        }
    }
}