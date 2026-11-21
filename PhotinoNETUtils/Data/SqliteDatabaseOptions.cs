using JetBrains.Annotations;

namespace PhotinoNetUtils.Data;

/// <summary>
///     Tuning for <see cref="SqliteDatabase" />. The defaults favour a local database that a desktop
///     app can reopen cheaply; a portable per-folder file turns WAL off instead.
/// </summary>
[PublicAPI]
public sealed record SqliteDatabaseOptions
{
    public static SqliteDatabaseOptions Default { get; } = new();

    /// <summary>
    ///     Keep native connections in ADO.NET's pool. Useful for one long-lived database; leave off
    ///     for a database per folder, where a pool would keep a cache alive for every folder visited.
    /// </summary>
    public bool Pooling { get; init; } = true;

    /// <summary>How long SQLite waits for another writer before it reports the database as busy.</summary>
    public int BusyTimeout { get; init; } = 5000;

    /// <summary>
    ///     Write-ahead logging. Faster for concurrent readers and the usual choice for a local
    ///     database, but a WAL keeps recent writes in <c>-wal</c>, which makes a copied folder
    ///     incomplete unless it is checkpointed first.
    /// </summary>
    public bool Wal { get; init; } = true;

    /// <summary>
    ///     Skip the fsync on commit. Only for data that can be rebuilt, such as a cache; a power loss
    ///     can then leave the database needing recovery.
    /// </summary>
    public bool SynchronousOff { get; init; }

    /// <summary>
    ///     When a read-write connection cannot be opened, such as on a read-only volume, retry the
    ///     read with a read-only connection.
    /// </summary>
    public bool ReadOnlyFallback { get; init; } = true;

    /// <summary>How many times a busy or locked operation is retried after the busy timeout.</summary>
    public int RetryCount { get; init; } = 10;

    /// <summary>Delay between those retries, in milliseconds.</summary>
    public int RetryDelay { get; init; } = 50;

    /// <summary>Command timeout passed to the connection string, in seconds.</summary>
    public int CommandTimeout { get; init; } = 30;
}