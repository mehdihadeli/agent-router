using AgentRouter.Storage.Configuration;
using AgentRouter.Storage.Secrets;
using AgentRouter.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentRouter.Storage;

/// <summary>Registers the gateway storage stack (SQLite, secrets, configuration document).</summary>
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

        var connectionFactory = string.IsNullOrWhiteSpace(options.DatabasePath)
            ? new SqliteConnectionFactory(paths)
            : new SqliteConnectionFactory(options.DatabasePath);

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
}
