using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;

namespace AgentRouter.Core.Storage.Configuration;

/// <summary>
/// Stores the configuration document as JSON and keeps its monotonic version in SQLite. Existing
/// SQLite configuration rows are migrated to the JSON file on first load.
/// </summary>
public sealed class SqliteConfigStore : IConfigStore
{
    internal const string ConfigKey = "config";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly GatewayPaths _paths;
    private readonly ConfigSecretSplitter _splitter;
    private readonly IGatewayTokenProvider _tokenProvider;
    private readonly ILogger<SqliteConfigStore> _logger;

    public SqliteConfigStore(
        SqliteConnectionFactory connectionFactory,
        GatewayPaths paths,
        ConfigSecretSplitter splitter,
        IGatewayTokenProvider tokenProvider,
        ILogger<SqliteConfigStore> logger
    )
    {
        _connectionFactory = connectionFactory;
        _paths = paths;
        _splitter = splitter;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<GatewayConfigDocument> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        var row = await ReadRowAsync(cancellationToken).ConfigureAwait(false);
        var isFirstRun = !File.Exists(_paths.ConfigPath) && row is null;

        GatewayConfigDocument document;
        if (File.Exists(_paths.ConfigPath))
        {
            var json = await File.ReadAllTextAsync(_paths.ConfigPath, cancellationToken)
                .ConfigureAwait(false);
            document = Deserialize(json);
        }
        else if (row is not null)
        {
            document = Deserialize(row.Value.Value);
            await WriteConfigFileAsync(document, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            document = GatewayConfigDocument.CreateDefault();
        }

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
        await WriteConfigFileAsync(json, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("$value", "config.json");
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
                ?? throw new InvalidDataException("The gateway configuration JSON is null.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Gateway configuration JSON is not valid.");
            throw new InvalidDataException(
                $"Gateway configuration file '{_paths.ConfigPath}' is not valid JSON.",
                exception
            );
        }
    }

    private async Task WriteConfigFileAsync(
        GatewayConfigDocument document,
        CancellationToken cancellationToken
    )
    {
        var plaintextFree = await _splitter
            .ExtractAsync(document, cancellationToken)
            .ConfigureAwait(false);
        var json = JsonSerializer.Serialize(plaintextFree, GatewayJson.Indented);
        await WriteConfigFileAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteConfigFileAsync(string json, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{_paths.ConfigPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _paths.ConfigPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
