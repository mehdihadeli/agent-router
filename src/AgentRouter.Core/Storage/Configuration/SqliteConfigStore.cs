using System.Text.Json;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Serialization;
using AgentRouter.Storage.Secrets;
using AgentRouter.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AgentRouter.Storage.Configuration;

/// <summary>
/// Stores the configuration document as a single <c>app_state</c> row. The row's <c>version</c>
/// column is the config version the proxy uses for cheap hot-reload detection.
/// </summary>
public sealed class SqliteConfigStore : IConfigStore
{
    internal const string ConfigKey = "config";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ConfigSecretSplitter _splitter;
    private readonly IGatewayTokenProvider _tokenProvider;
    private readonly ILogger<SqliteConfigStore> _logger;

    public SqliteConfigStore(
        SqliteConnectionFactory connectionFactory,
        ConfigSecretSplitter splitter,
        IGatewayTokenProvider tokenProvider,
        ILogger<SqliteConfigStore> logger
    )
    {
        _connectionFactory = connectionFactory;
        _splitter = splitter;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<GatewayConfigDocument> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        var row = await ReadRowAsync(cancellationToken).ConfigureAwait(false);
        var isFirstRun = row is null;

        var document = row is null
            ? GatewayConfigDocument.CreateDefault()
            : Deserialize(row.Value.Value);

        ConfigSecretSplitter.Normalize(document);
        await _splitter.HydrateAsync(document, cancellationToken).ConfigureAwait(false);

        // Guarantees a usable gateway token even before the first explicit save.
        document.Server.AuthToken = await _tokenProvider
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);

        if (isFirstRun)
        {
            _logger.LogInformation("Created a default gateway configuration document.");
            await SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    public async Task SaveAsync(
        GatewayConfigDocument document,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        ConfigSecretSplitter.Normalize(document);
        var plaintextFree = await _splitter
            .ExtractAsync(document, cancellationToken)
            .ConfigureAwait(false);

        var json = JsonSerializer.Serialize(plaintextFree, GatewayJson.Indented);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_state (key, value, version, updated_at)
            VALUES ($key, $value, 1, $updatedAt)
            ON CONFLICT (key) DO UPDATE SET
                value = excluded.value,
                version = app_state.version + 1,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", ConfigKey);
        command.Parameters.AddWithValue("$value", json);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetConfigVersionAsync(CancellationToken cancellationToken = default)
    {
        var row = await ReadRowAsync(cancellationToken).ConfigureAwait(false);

        return row?.Version ?? 0;
    }

    private async Task<(string Value, long Version)?> ReadRowAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value, version FROM app_state WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", ConfigKey);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (reader.GetString(0), reader.GetInt64(1));
    }

    private GatewayConfigDocument Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GatewayConfigDocument>(json, GatewayJson.Options)
                ?? GatewayConfigDocument.CreateDefault();
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Stored gateway configuration is not valid JSON; falling back to defaults."
            );

            return GatewayConfigDocument.CreateDefault();
        }
    }
}
