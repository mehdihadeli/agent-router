using AgentRouter.Storage.Secrets;
using Xunit;

namespace AgentRouter.Tests;

public class MasterKeyProviderTests
{
    /// <summary>Whitespace means "no environment override", which makes these tests deterministic.</summary>
    private const string NoEnvironmentOverride = "   ";

    private static readonly string ValidHex = new('a', MasterKey.KeySizeBytes * 2);

    [Fact]
    public void Resolve_Uses_Environment_Override()
    {
        using var workspace = new TempWorkspace();

        var key = MasterKeyProvider.Resolve(workspace.Paths, ValidHex);

        Assert.Equal(MasterKey.KeySizeBytes, key.Value.Length);
        Assert.Equal(Convert.FromHexString(ValidHex), key.Value);
        Assert.False(File.Exists(workspace.Paths.MasterKeyPath));
    }

    [Fact]
    public void Resolve_Generates_And_Reuses_Key_File()
    {
        using var workspace = new TempWorkspace();

        var first = MasterKeyProvider.Resolve(workspace.Paths, NoEnvironmentOverride);

        Assert.True(File.Exists(workspace.Paths.MasterKeyPath));
        Assert.Equal(MasterKey.KeySizeBytes, first.Value.Length);

        var second = MasterKeyProvider.Resolve(workspace.Paths, NoEnvironmentOverride);

        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async Task Resolve_ConcurrentFirstRunUsesOneCompleteKey()
    {
        using var workspace = new TempWorkspace();
        var keys = await Task.WhenAll(Enumerable.Range(0, 16).Select(iteration => Task.Run(
            () => MasterKeyProvider.Resolve(workspace.Paths, NoEnvironmentOverride), TestContext.Current.CancellationToken)));
        var persisted = Convert.FromHexString(await File.ReadAllTextAsync(workspace.Paths.MasterKeyPath, TestContext.Current.CancellationToken));
        Assert.Equal(MasterKey.KeySizeBytes, persisted.Length);
        Assert.All(keys, key => Assert.Equal(persisted, key.Value));
        Assert.Single(Directory.GetFiles(workspace.Root));
    }

    [Fact]
    public void Resolve_Throws_For_Malformed_Environment_Key()
    {
        using var workspace = new TempWorkspace();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MasterKeyProvider.Resolve(workspace.Paths, "not-hex")
        );

        Assert.Contains("hex key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Throws_When_Key_File_Is_Malformed()
    {
        using var workspace = new TempWorkspace();
        workspace.Paths.EnsureCreated();
        File.WriteAllText(workspace.Paths.MasterKeyPath, "garbage");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MasterKeyProvider.Resolve(workspace.Paths, NoEnvironmentOverride)
        );

        Assert.Contains("master key file", exception.Message, StringComparison.Ordinal);
    }
}
