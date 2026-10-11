using AgentRouter.Contracts.Defaults;
using AgentRouter.Storage.Secrets;
using Xunit;

namespace AgentRouter.Tests;

public class GatewayTokenProviderTests
{
    [Fact]
    public async Task GetAsync_Generates_Prefixed_Token_On_First_Use()
    {
        using var harness = await StorageHarness.CreateAsync();

        var token = await harness.TokenProvider.GetAsync();

        Assert.StartsWith(GatewayDefaults.AuthTokenPrefix, token, StringComparison.Ordinal);
        Assert.Equal(GatewayDefaults.AuthTokenPrefix.Length + 32, token.Length);
        Assert.True(await harness.SecretStore.ContainsAsync(SecretKeys.ServerAuthToken));
    }

    [Fact]
    public async Task GetAsync_Is_Stable_Across_Calls()
    {
        using var harness = await StorageHarness.CreateAsync();

        var first = await harness.TokenProvider.GetAsync();
        var second = await harness.TokenProvider.GetAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RotateAsync_Produces_A_Different_Token()
    {
        using var harness = await StorageHarness.CreateAsync();
        var original = await harness.TokenProvider.GetAsync();

        var rotated = await harness.TokenProvider.RotateAsync();

        Assert.NotEqual(original, rotated);
        Assert.Equal(rotated, await harness.TokenProvider.GetAsync());
    }
}
