using Microsoft.Data.Sqlite;

namespace AgentRouter.Storage.Sqlite;

/// <summary>
/// Creates configured SQLite connections. Every connection applies the pragmas the gateway
/// depends on, so callers never have to remember them.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(GatewayPaths paths)
        : this(paths.DatabasePath) { }

    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>Creates a new, unopened connection.</summary>
    public SqliteConnection Create() => new(_connectionString);

    /// <summary>Opens a connection and applies the per-connection pragmas.</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = Create();

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }

    internal static async Task ApplyPragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        // WAL is persistent per database, but setting it here keeps a freshly created database
        // consistent without a separate migration step.
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA synchronous=NORMAL;", cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
