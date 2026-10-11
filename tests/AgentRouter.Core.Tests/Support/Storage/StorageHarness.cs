using System.Security.Cryptography;
using AgentRouter.Storage;
using AgentRouter.Storage.Configuration;
using AgentRouter.Storage.Secrets;
using AgentRouter.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentRouter.Tests;

/// <summary>
/// Disposable temporary data directory used by storage tests.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ccg-storage-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public GatewayPaths Paths => new(Root);

    public void Dispose()
    {
        // SQLite keeps pooled handles open, so the file stays locked until pools are cleared.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning as above.
        }
    }
}

/// <summary>
/// Fully wired storage stack over a temporary SQLite database.
/// </summary>
internal sealed class StorageHarness : IDisposable
{
    private readonly TempWorkspace _workspace;

    private StorageHarness(
        TempWorkspace workspace,
        SqliteConnectionFactory connectionFactory,
        AesGcmSecretStore secretStore,
        GatewayTokenProvider tokenProvider,
        ConfigSecretSplitter splitter,
        SqliteConfigStore configStore
    )
    {
        _workspace = workspace;
        ConnectionFactory = connectionFactory;
        SecretStore = secretStore;
        TokenProvider = tokenProvider;
        Splitter = splitter;
        ConfigStore = configStore;
    }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public AesGcmSecretStore SecretStore { get; }

    public GatewayTokenProvider TokenProvider { get; }

    public ConfigSecretSplitter Splitter { get; }

    public SqliteConfigStore ConfigStore { get; }

    public GatewayPaths Paths => _workspace.Paths;

    public static async Task<StorageHarness> CreateAsync(
        byte[]? masterKey = null,
        CancellationToken cancellationToken = default
    )
    {
        var workspace = new TempWorkspace();
        var paths = workspace.Paths;
        var connectionFactory = new SqliteConnectionFactory(paths);

        var database = new GatewayDatabase(connectionFactory, NullLogger<GatewayDatabase>.Instance);
        await database.InitializeAsync(cancellationToken);

        var secretStore = new AesGcmSecretStore(
            connectionFactory,
            new MasterKey(masterKey ?? RandomNumberGenerator.GetBytes(MasterKey.KeySizeBytes)),
            NullLogger<AesGcmSecretStore>.Instance
        );

        var tokenProvider = new GatewayTokenProvider(secretStore);
        var splitter = new ConfigSecretSplitter(secretStore);
        var configStore = new SqliteConfigStore(
            connectionFactory,
            splitter,
            tokenProvider,
            NullLogger<SqliteConfigStore>.Instance
        );

        return new StorageHarness(
            workspace,
            connectionFactory,
            secretStore,
            tokenProvider,
            splitter,
            configStore
        );
    }

    public void Dispose() => _workspace.Dispose();
}
