using AgentRouter.Contracts.Defaults;
using AgentRouter.Storage;
using Xunit;

namespace AgentRouter.Tests;

public class GatewayPathsTests
{
    [Fact]
    public void Resolve_Uses_Explicit_Override()
    {
        using var workspace = new TempWorkspace();

        var paths = GatewayPaths.Resolve(workspace.Root);

        Assert.Equal(Path.GetFullPath(workspace.Root), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "gateway.sqlite"), paths.DatabasePath);
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
    }
}
