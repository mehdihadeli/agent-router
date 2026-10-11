using AgentRouter.Contracts.Configuration;
using AgentRouter.Storage;
using AgentRouter.Storage.Configuration;
using AgentRouter.Storage.Secrets;
using AgentRouter.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Tests;

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
