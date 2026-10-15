using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentRouter.Core.Storage;

/// <summary>Registers the gateway storage stack (JSON configuration, SQLite runtime data, secrets).</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds the storage services. Call <see cref="GatewayDatabase.InitializeAsync"/> once during
    /// startup to create or migrate the schema.
    /// </summary>
    public static IServiceCollection AddGatewayStorage(
        this IServiceCollection services,
        Action<StorageOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        // The storage stack always has somewhere to log. Hosts that already configured logging
        // keep their providers; a bare consumer still gets a working (no-op) logger.
        services.AddLogging();

        var options = new StorageOptions();
        configure?.Invoke(options);

        var paths = GatewayPaths.Resolve(options.Directory);
        paths.EnsureCreated();

        var masterKey = MasterKeyProvider.Resolve(paths, options.MasterKey);

        var databasePath = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(databasePath))
            databasePath = LoadConfiguredDatabasePath(paths);
        if (!string.IsNullOrWhiteSpace(databasePath) && !Path.IsPathRooted(databasePath))
            databasePath = Path.Combine(paths.RootDirectory, databasePath);

        var connectionFactory = string.IsNullOrWhiteSpace(databasePath)
            ? new SqliteConnectionFactory(paths)
            : new SqliteConnectionFactory(databasePath);

        services.TryAddSingleton(paths);
        services.TryAddSingleton(masterKey);
        services.TryAddSingleton(connectionFactory);
        services.TryAddSingleton(options);
        services.TryAddSingleton<GatewayDatabase>();
        services.TryAddSingleton<ISecretStore, AesGcmSecretStore>();
        services.TryAddSingleton<IGatewayTokenProvider, GatewayTokenProvider>();
        services.TryAddSingleton<ConfigSecretSplitter>();
        services.TryAddSingleton<IConfigStore, SqliteConfigStore>();

        return services;
    }

    private static string? LoadConfiguredDatabasePath(GatewayPaths paths)
    {
        var environmentPath = Environment.GetEnvironmentVariable("Storage__DatabasePath");
        if (!string.IsNullOrWhiteSpace(environmentPath))
            return environmentPath;

        if (!File.Exists(paths.ConfigPath))
            return null;

        var json = File.ReadAllText(paths.ConfigPath);
        var document = JsonSerializer.Deserialize<GatewayConfigDocument>(json, GatewayJson.Options);
        return document?.Storage.DatabasePath;
    }
}
