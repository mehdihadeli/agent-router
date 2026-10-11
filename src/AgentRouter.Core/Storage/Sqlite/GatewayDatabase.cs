using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AgentRouter.Storage.Sqlite;

/// <summary>Creates the schema and applies pending migrations at startup.</summary>
public sealed class GatewayDatabase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<GatewayDatabase> _logger;

    public GatewayDatabase(
        SqliteConnectionFactory connectionFactory,
        ILogger<GatewayDatabase> logger
    )
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>Applies every migration newer than the database's current <c>user_version</c>.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        var current = await ReadUserVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        if (current >= DatabaseSchema.CurrentVersion)
        {
            return;
        }

        for (var target = current + 1; target <= DatabaseSchema.CurrentVersion; target++)
        {
            var script = DatabaseSchema.Migrations[target - 1];

            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = script;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = (SqliteTransaction)transaction;
            // PRAGMA does not accept parameters, so the integer is inlined from a validated range.
            versionCommand.CommandText = $"PRAGMA user_version={target};";
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Applied gateway storage migration to version {Version}.",
                target
            );
        }

        _logger.LogDebug(
            "Gateway storage ready at {DatabasePath} (version {Version}).",
            _connectionFactory.DatabasePath,
            DatabaseSchema.CurrentVersion
        );
    }

    private static async Task<int> ReadUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is null or DBNull
            ? 0
            : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
