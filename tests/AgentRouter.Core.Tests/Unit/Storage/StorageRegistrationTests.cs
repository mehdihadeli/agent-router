using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Storage;

public class StorageRegistrationTests
{
    private static readonly string ValidHex = new('b', MasterKey.KeySizeBytes * 2);

    [Fact]
    public void AddGatewayStorage_Resolves_Every_Service()
    {
        using var workspace = new TempWorkspace();
        var services = new ServiceCollection();

        services.AddGatewayStorage(options =>
        {
            options.Directory = workspace.Root;
            options.MasterKey = ValidHex;
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<GatewayPaths>());
        Assert.NotNull(provider.GetRequiredService<StorageOptions>());
        Assert.NotNull(provider.GetRequiredService<SqliteConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<GatewayDatabase>());
        Assert.NotNull(provider.GetRequiredService<ISecretStore>());
        Assert.NotNull(provider.GetRequiredService<IGatewayTokenProvider>());
        Assert.NotNull(provider.GetRequiredService<ConfigSecretSplitter>());
        Assert.NotNull(provider.GetRequiredService<IConfigStore>());
    }

    [Fact]
    public void AddGatewayStorage_Creates_The_Data_Directory()
    {
        using var workspace = new TempWorkspace();
        var target = Path.Combine(workspace.Root, "data");
        var services = new ServiceCollection();

        services.AddGatewayStorage(options =>
        {
            options.Directory = target;
            options.MasterKey = ValidHex;
        });

        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void AddGatewayStorage_Uses_Json_Database_Path_Relative_To_Data_Directory()
    {
        using var workspace = new TempWorkspace();
        var configured = Path.Combine(workspace.Root, "runtime", "gateway.db");
        File.WriteAllText(
            Path.Combine(workspace.Root, "config.json"),
            System.Text.Json.JsonSerializer.Serialize(
                new GatewayConfigDocument
                {
                    Storage = new StorageConfig
                    {
                        DatabasePath = Path.GetRelativePath(workspace.Root, configured),
                    },
                },
                GatewayJson.Indented
            )
        );

        var services = new ServiceCollection();
        services.AddGatewayStorage(options =>
        {
            options.Directory = workspace.Root;
            options.MasterKey = ValidHex;
        });

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            Path.GetFullPath(configured),
            provider.GetRequiredService<SqliteConnectionFactory>().DatabasePath
        );
    }

    [Fact]
    public void Explicit_Database_Path_Overrides_Json_Database_Path()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Root, "config.json"),
            "{ \"Storage\": { \"DatabasePath\": \"json.db\" } }"
        );
        var explicitPath = Path.Combine(workspace.Root, "explicit.db");

        var services = new ServiceCollection();
        services.AddGatewayStorage(options =>
        {
            options.Directory = workspace.Root;
            options.DatabasePath = explicitPath;
            options.MasterKey = ValidHex;
        });

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            Path.GetFullPath(explicitPath),
            provider.GetRequiredService<SqliteConnectionFactory>().DatabasePath
        );
    }

    [Fact]
    public async Task Resolved_Config_Store_Is_Usable_After_Initialization()
    {
        using var workspace = new TempWorkspace();
        var services = new ServiceCollection();

        services.AddGatewayStorage(options =>
        {
            options.Directory = workspace.Root;
            options.MasterKey = ValidHex;
        });

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<GatewayDatabase>().InitializeAsync();

        var document = await provider.GetRequiredService<IConfigStore>().LoadAsync();

        Assert.Equal(ModelMode.Single, document.ModelMode);

        SqliteConnection.ClearAllPools();
    }
}
