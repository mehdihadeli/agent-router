using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Tests.Support.Storage;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Storage;

[CollectionDefinition("GatewayPathsEnvironment", DisableParallelization = true)]
public class GatewayPathsEnvironmentCollection { }

[Collection("GatewayPathsEnvironment")]
public class GatewayPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_Defaults_To_AgentRouter_In_User_Profile(string? configuredDirectory)
    {
        using var workspace = new TempWorkspace();
        WithEnvironment(
            new Dictionary<string, string?>
            {
                [GatewayDefaults.ConfigDirectoryEnvironmentVariable] = configuredDirectory,
                ["APPDATA"] = Path.Combine(workspace.Root, "ignored-appdata"),
                ["XDG_CONFIG_HOME"] = Path.Combine(workspace.Root, "ignored-xdg"),
            },
            () =>
            {
                var expected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".agent-router"
                );
                var paths = GatewayPaths.Resolve();
                Assert.Equal(Path.GetFullPath(expected), paths.RootDirectory);
                Assert.Equal(Path.Combine(expected, "gateway.sqlite"), paths.DatabasePath);
                Assert.Equal(Path.Combine(expected, "config.json"), paths.ConfigPath);
                Assert.Equal(Path.Combine(expected, "secret.key"), paths.MasterKeyPath);
                Assert.Equal(Path.Combine(expected, "provider-logos"), paths.ProviderLogoDirectory);
                Assert.Equal(Path.Combine(expected, "gateway.pid"), paths.PidFilePath);
                Assert.Equal(Path.Combine(expected, "gateway.log"), paths.LogFilePath);
                Assert.Equal(
                    Path.Combine(expected, "settings.json"),
                    GatewaySettingsLoader.GlobalFile
                );
                Assert.False(Directory.Exists(Path.Combine(workspace.Root, "ignored-appdata")));
                Assert.False(Directory.Exists(Path.Combine(workspace.Root, "ignored-xdg")));
            }
        );
    }

    [Fact]
    public void Resolve_Environment_Override_Remains_Above_Default_And_Below_Explicit_Path()
    {
        using var workspace = new TempWorkspace();
        var configured = Path.Combine(workspace.Root, "environment");
        var explicitPath = Path.Combine(workspace.Root, "explicit");
        WithEnvironment(
            new Dictionary<string, string?>
            {
                [GatewayDefaults.ConfigDirectoryEnvironmentVariable] = configured,
            },
            () =>
            {
                Assert.Equal(configured, GatewayPaths.Resolve().RootDirectory);
                Assert.Equal(configured, GatewayPaths.Resolve(" ").RootDirectory);
                Assert.Equal(explicitPath, GatewayPaths.Resolve(explicitPath).RootDirectory);
                Assert.False(Directory.Exists(configured));
                Assert.False(Directory.Exists(explicitPath));
            }
        );
    }

    private static void WithEnvironment(Dictionary<string, string?> variables, Action action)
    {
        var previous = variables.ToDictionary(
            entry => entry.Key,
            entry => Environment.GetEnvironmentVariable(entry.Key)
        );
        try
        {
            foreach (var entry in variables)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
            action();
        }
        finally
        {
            foreach (var entry in previous)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    [Fact]
    public void Resolve_GlobalJson_Uses_The_Shared_Environment_Override()
    {
        using var workspace = new TempWorkspace();
        var configured = Path.Combine(workspace.Root, "configured");
        WithEnvironment(
            new Dictionary<string, string?>
            {
                [GatewayDefaults.ConfigDirectoryEnvironmentVariable] = configured,
            },
            () =>
            {
                var provider = new AgentRouterConfigConfigurationProvider(null, true);
                provider.Load();
                Assert.True(File.Exists(Path.Combine(configured, "config.json")));
                Assert.Equal(configured, GatewayPaths.Resolve().RootDirectory);
                Assert.Equal(
                    Path.Combine(configured, "settings.json"),
                    GatewaySettingsLoader.GlobalFile
                );
                Assert.True(provider.TryGet("GatewayBaseAddress", out var address));
                Assert.Equal("http://localhost:4000", address);
            }
        );
    }

    [Fact]
    public void Resolve_Uses_Explicit_Override()
    {
        using var workspace = new TempWorkspace();

        var paths = GatewayPaths.Resolve(workspace.Root);

        Assert.Equal(Path.GetFullPath(workspace.Root), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "gateway.sqlite"), paths.DatabasePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "config.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "secret.key"), paths.MasterKeyPath);
    }

    [Fact]
    public void Resolve_Throws_For_Blank_Directory()
    {
        Assert.Throws<ArgumentException>(() => new GatewayPaths("  "));
    }

    [Fact]
    public void EnsureCreated_Creates_Root_Directory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ccg-storage-tests",
            Guid.NewGuid().ToString("n"),
            "nested"
        );

        try
        {
            var paths = new GatewayPaths(root);
            Assert.False(Directory.Exists(root));

            paths.EnsureCreated();

            Assert.True(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BindHost_Default_Is_Loopback()
    {
        Assert.Equal("127.0.0.1", GatewayDefaults.LoopbackHost);
        Assert.Equal(49250, GatewayDefaults.ProxyPort);
        Assert.Equal(6767, GatewayDefaults.PanelPort);
        Assert.Equal("AR_BIND_HOST", GatewayDefaults.BindHostEnvironmentVariable);
        Assert.Equal("AR_SECRET_KEY", GatewayDefaults.SecretKeyEnvironmentVariable);
        Assert.Equal("AR_CONFIG_DIR", GatewayDefaults.ConfigDirectoryEnvironmentVariable);
        Assert.Equal("AR_PANEL_ORIGINS", GatewayDefaults.PanelOriginsEnvironmentVariable);
    }
}
