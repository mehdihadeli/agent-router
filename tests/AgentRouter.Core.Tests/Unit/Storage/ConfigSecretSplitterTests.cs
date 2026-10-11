using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Defaults;
using AgentRouter.Storage.Configuration;
using AgentRouter.Storage.Secrets;
using Xunit;

namespace AgentRouter.Tests;

public class ConfigSecretSplitterTests
{
    [Fact]
    public void Mask_Blanks_AuthToken_And_Previews_ApiKey()
    {
        var document = GatewayConfigDocument.CreateDefault();
        document.Server.AuthToken = "sk_deadbeef";
        document.Providers["demo"] = new ProviderConfig { ApiKey = "abcdef123456" };

        var masked = ConfigSecretSplitter.Mask(document);

        Assert.Null(masked.Server.AuthToken);
        Assert.Equal(AesGcmSecretStore.MaskToken + "3456", masked.Providers["demo"].ApiKey);
    }

    [Fact]
    public void Mask_Does_Not_Mutate_The_Source_Document()
    {
        var document = GatewayConfigDocument.CreateDefault();
        document.Server.AuthToken = "sk_deadbeef";
        document.Providers["demo"] = new ProviderConfig { ApiKey = "abcdef123456" };

        ConfigSecretSplitter.Mask(document);

        Assert.Equal("sk_deadbeef", document.Server.AuthToken);
        Assert.Equal("abcdef123456", document.Providers["demo"].ApiKey);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("ab", AesGcmSecretStore.MaskToken)]
    [InlineData("abcd", AesGcmSecretStore.MaskToken)]
    [InlineData("abcde", AesGcmSecretStore.MaskToken + "bcde")]
    public void MaskSecret_Reduces_Values_To_A_Preview(string? input, string? expected)
    {
        Assert.Equal(expected, ConfigSecretSplitter.MaskSecret(input));
    }

    [Fact]
    public void MaskSecret_Leaves_An_Already_Masked_Value_Unchanged()
    {
        var masked = AesGcmSecretStore.MaskToken + "3456";

        Assert.Equal(masked, ConfigSecretSplitter.MaskSecret(masked));
    }

    [Fact]
    public async Task Extract_Does_Not_Mutate_The_Caller_Document()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = GatewayConfigDocument.CreateDefault();
        document.Server.AuthToken = "sk_test";
        document.Providers["demo"] = new ProviderConfig { ApiKey = "plain-key-123456" };

        var stripped = await harness.Splitter.ExtractAsync(document);

        Assert.Equal("plain-key-123456", document.Providers["demo"].ApiKey);
        Assert.Equal("sk_test", document.Server.AuthToken);
        Assert.Null(stripped.Providers["demo"].ApiKey);
        Assert.Null(stripped.Server.AuthToken);
    }

    [Fact]
    public async Task Extract_Then_Hydrate_RoundTrips_Every_Secret()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = GatewayConfigDocument.CreateDefault();
        document.Providers["demo"] = new ProviderConfig
        {
            ApiKey = "plain-key-123456",
            OAuth = new OAuthTokens
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                CopilotToken = "copilot-token",
            },
        };

        await harness.Splitter.ExtractAsync(document);

        var reloaded = GatewayConfigDocument.CreateDefault();
        reloaded.Providers["demo"] = new ProviderConfig { OAuth = new OAuthTokens() };
        await harness.Splitter.HydrateAsync(reloaded);

        var provider = reloaded.Providers["demo"];
        Assert.Equal("plain-key-123456", provider.ApiKey);
        Assert.Equal("access-token", provider.OAuth!.AccessToken);
        Assert.Equal("refresh-token", provider.OAuth.RefreshToken);
        Assert.Equal("copilot-token", provider.OAuth.CopilotToken);
    }

    [Fact]
    public void Normalize_Repairs_Null_Collections()
    {
        var document = new GatewayConfigDocument
        {
            Providers = null!,
            ModelFallbacks = null!,
            Routing = null!,
            Thinking = null!,
            WebTools = null!,
            Proxy = null!,
            TokenSavers = null!,
            PanelSettings = null!,
        };

        var normalized = ConfigSecretSplitter.Normalize(document);

        Assert.NotNull(normalized.Providers);
        Assert.NotNull(normalized.ModelFallbacks);
        Assert.NotNull(normalized.Routing);
        Assert.NotNull(normalized.Thinking);
        Assert.NotNull(normalized.WebTools);
        Assert.NotNull(normalized.Proxy);
        Assert.NotNull(normalized.TokenSavers);
        Assert.NotNull(normalized.PanelSettings);
        Assert.NotNull(normalized.PanelSettings.FavoriteProviders);
    }

    [Fact]
    public void Default_Document_Uses_Gateway_Defaults()
    {
        var document = GatewayConfigDocument.CreateDefault();

        Assert.Equal(GatewayDefaults.ProxyPort, document.Server.ProxyPort);
        Assert.Equal(GatewayDefaults.PanelPort, document.Server.PanelPort);
        Assert.Equal(ModelMode.Single, document.ModelMode);
        Assert.Empty(document.Providers);
    }
}
