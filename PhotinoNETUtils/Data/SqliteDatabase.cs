using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils.Data;

/// <summary>
///     A small SQLite wrapper for the way a desktop app actually uses a database: many concurrent
///     reads, one writer per file, and another process potentially touching the same file.
/// </summary>
/// <remarks>
///     <para>
///         The write gate is per database file, not global: two different databases can be written at
///         the same time. The same file never has two writers from this process, and a busy or locked
///         operation is retried after SQLite's own busy timeout, so an external process only makes a
///         write wait instead of failing.
///     </para>
///     <para>
///         Reads take no gate. SQLite allows several readers, and the busy timeout covers the moment
///         a rollback-journal writer commits. A database in WAL mode lets readers continue while the
///         writer works.
///     </para>
///     <para>
///         <see cref="WriteAsync{T}" /> may run its callbacks more than once when SQLite reports the
///         database as busy, so keep them free of side effects outside the database.
///     </para>
/// </remarks>
[PublicAPI]
public sealed class SqliteDatabase
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteDatabase(string path, SqliteDatabaseOptions? options = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        _options = options ?? SqliteDatabaseOptions.Default;
    }

    /// <summary>The absolute path this instance names.</summary>
    public string Path { get; }

    /// <summary>Whether the database file exists right now.</summary>
    public bool Exists => File.Exists(Path);

    public SqliteDatabaseOptions Options => _options;

    /// <summary>
    ///     Runs a read on its own connection. No write gate is taken, so readers never wait for one
    ///     another; SQLite's file locks and the busy timeout handle a writer in this or another process.
    /// </summary>
    public Task<Result<T>> ReadAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default) =>
        RetryAsync(async () =>
        {
            if (!File.Exists(Path))
                throw new FileNotFoundException("The database does not exist.", Path);

            await using var connection = await OpenCoreAsync(false, cancellationToken).ConfigureAwait(false);
            return await read(connection, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    ///     Runs one write transaction while holding this file's gate. <paramref name="prepare" /> runs
    ///     before the transaction; use it for work that must not be part of the write, such as importing
    ///     another database.
    /// </summary>
    public async Task<Result<T>> WriteAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> write,
        Func<CancellationToken, Task>? prepare = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await SqliteFileGate.AcquireAsync(Path, cancellationToken).ConfigureAwait(false);

        return await RetryAsync(async () =>
        {
            if (prepare is not null)
                await prepare(cancellationToken).ConfigureAwait(false);

            await using var connection = await OpenCoreAsync(true, cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction(false);

            var value = await write(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return value;
        }, cancellationToken);
    }

    /// <summary>Runs one write transaction that has no result.</summary>
    public Task<Result<Unit>> WriteAsync(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> write,
        Func<CancellationToken, Task>? prepare = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (connection, transaction, token) =>
        {
            await write(connection, transaction, token).ConfigureAwait(false);
            return Unit.Value;
        }, prepare, cancellationToken);

    /// <summary>
    ///     Holds this file's write gate around arbitrary work, such as deleting the database and its
    ///     side files. Nothing is opened; the callback may open its own connections.
    /// </summary>
    public async Task<Result<T>> ExclusiveAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        using var lease = await SqliteFileGate.AcquireAsync(Path, cancellationToken).ConfigureAwait(false);
        return await RetryAsync(() => action(cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Opens a read connection. Callers dispose it. Reads do not take the write gate.
    /// </summary>
    public Task<Result<SqliteConnection>> OpenReadAsync(CancellationToken cancellationToken = default) =>
        RetryAsync(() => OpenCoreAsync(false, cancellationToken), cancellationToken);

    /// <summary>
    ///     Opens a write connection without taking the gate. Use <see cref="WriteAsync{T}" />, or call
    ///     this only inside <see cref="ExclusiveAsync{T}" /> when a flow needs more than one connection.
    /// </summary>
    public Task<Result<SqliteConnection>> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        RetryAsync(() => OpenCoreAsync(true, cancellationToken), cancellationToken);

    private async Task<SqliteConnection> OpenCoreAsync(bool write, CancellationToken cancellationToken)
    {
        if (write && System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        var connection = CreateConnection(write ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureAsync(connection, write, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (Exception exception) when (!write && _options.ReadOnlyFallback &&
                                          exception is not OperationCanceledException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var fallback = CreateConnection(SqliteOpenMode.ReadOnly);
        try
        {
            await fallback.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureAsync(fallback, false, cancellationToken).ConfigureAwait(false);
            return fallback;
        }
        catch
        {
            await fallback.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private SqliteConnection CreateConnection(SqliteOpenMode mode) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = mode,
            Pooling = _options.Pooling,
            DefaultTimeout = _options.CommandTimeout
        }.ToString());

    private async Task ConfigureAsync(SqliteConnection connection, bool write, CancellationToken cancellationToken)
    {
        var synchronous = _options.SynchronousOff ? "OFF" : "NORMAL";
        var journal = _options.Wal ? "WAL" : "DELETE";

        await using var command = connection.CreateCommand();
        command.CommandText = write
            ? $"PRAGMA busy_timeout = {_options.BusyTimeout}; " +
              $"PRAGMA synchronous = {synchronous}; " +
              $"PRAGMA journal_mode = {journal};"
            : $"PRAGMA busy_timeout = {_options.BusyTimeout}; " +
              $"PRAGMA synchronous = {synchronous}; " +
              "PRAGMA query_only = ON;";

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<T>> RetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return Result<T>.Success(await action().ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SqliteException exception) when (IsBusy(exception) && attempt < _options.RetryCount)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return Result<T>.Failure(exception);
            }
        }
    }

    /// <summary>SQLITE_BUSY (5) and SQLITE_LOCKED (6), including their extended codes.</summary>
    private static bool IsBusy(SqliteException exception) => (exception.SqliteErrorCode & 0xFF) is 5 or 6;
}
