using System.Text.Json;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Defaults;
using AgentRouter.Contracts.Serialization;
using AgentRouter.Storage.Configuration;
using Xunit;

namespace AgentRouter.Tests;

public class SqliteConfigStoreTests
{
    private const string ProviderId = "demo";
    private const string PlaintextApiKey = "plain-key-123456";

    [Fact]
    public async Task Load_Creates_Defaults_And_Auth_Token_On_First_Run()
    {
        using var harness = await StorageHarness.CreateAsync();

        var document = await harness.ConfigStore.LoadAsync();

        Assert.Equal(GatewayDefaults.ProxyPort, document.Server.ProxyPort);
        Assert.Equal(GatewayDefaults.PanelPort, document.Server.PanelPort);
        Assert.StartsWith(
            GatewayDefaults.AuthTokenPrefix,
            document.Server.AuthToken,
            StringComparison.Ordinal
        );
        Assert.Equal(1, await harness.ConfigStore.GetConfigVersionAsync());
    }

    [Fact]
    public async Task Save_Increments_Config_Version()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = await harness.ConfigStore.LoadAsync();
        var initial = await harness.ConfigStore.GetConfigVersionAsync();

        await harness.ConfigStore.SaveAsync(document);
        var afterFirstSave = await harness.ConfigStore.GetConfigVersionAsync();

        await harness.ConfigStore.SaveAsync(document);
        var afterSecondSave = await harness.ConfigStore.GetConfigVersionAsync();

        Assert.Equal(initial + 1, afterFirstSave);
        Assert.Equal(initial + 2, afterSecondSave);
    }

    [Fact]
    public async Task Save_Persists_No_Plaintext_Secrets()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = await harness.ConfigStore.LoadAsync();
        document.Providers[ProviderId] = new ProviderConfig { ApiKey = PlaintextApiKey };

        await harness.ConfigStore.SaveAsync(document);

        var storedJson = await ReadStoredConfigJsonAsync(harness);

        Assert.DoesNotContain(PlaintextApiKey, storedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", storedJson, StringComparison.Ordinal);
        Assert.Equal(
            PlaintextApiKey,
            await harness.SecretStore.GetAsync(SecretKeys.ProviderApiKey(ProviderId))
        );
    }

    [Fact]
    public async Task Load_Hydrates_A_Saved_Api_Key()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = await harness.ConfigStore.LoadAsync();
        document.Providers[ProviderId] = new ProviderConfig
        {
            ApiKey = PlaintextApiKey,
            BaseUrl = "https://example.test/v1",
        };

        await harness.ConfigStore.SaveAsync(document);

        var reloaded = await harness.ConfigStore.LoadAsync();

        Assert.Equal(PlaintextApiKey, reloaded.Providers[ProviderId].ApiKey);
        Assert.Equal("https://example.test/v1", reloaded.Providers[ProviderId].BaseUrl);
    }

    [Fact]
    public async Task Save_Of_A_Masked_Document_Keeps_The_Existing_Secret()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = await harness.ConfigStore.LoadAsync();
        document.Providers[ProviderId] = new ProviderConfig { ApiKey = PlaintextApiKey };
        await harness.ConfigStore.SaveAsync(document);

        var masked = ConfigSecretSplitter.Mask(await harness.ConfigStore.LoadAsync());
        Assert.DoesNotContain(
            PlaintextApiKey,
            masked.Providers[ProviderId].ApiKey!,
            StringComparison.Ordinal
        );

        await harness.ConfigStore.SaveAsync(masked);

        var reloaded = await harness.ConfigStore.LoadAsync();
        Assert.Equal(PlaintextApiKey, reloaded.Providers[ProviderId].ApiKey);
    }

    [Fact]
    public async Task Load_RoundTrips_Every_Configuration_Section()
    {
        using var harness = await StorageHarness.CreateAsync();
        var document = await harness.ConfigStore.LoadAsync();
        document.Providers[ProviderId] = new ProviderConfig
        {
            Enabled = true,
            Compatibility = ProviderCompatibility.Anthropic,
            MaxConcurrency = 4,
            RateLimit = 60,
            RateWindow = 30,
            Models = ["alpha", "beta"],
            Custom = new ProviderCustomMetadata { Label = "Demo", Slug = ProviderId },
        };
        document.Routing.Opus = new RoutingRule
        {
            Enabled = true,
            ProviderId = ProviderId,
            Model = "alpha",
        };
        document.TokenSavers.CavemanEnabled = true;
        document.TokenSavers.CavemanLevel = CavemanLevel.Ultra;
        document.ModelMode = ModelMode.Chains;
        document.ModelFallbacks.Add(
            new ModelFallbackChain
            {
                Id = "chain_1",
                Name = "Rescue",
                Slug = "rescue",
                Models =
                [
                    new ModelFallbackTarget { ProviderId = ProviderId, Model = "alpha" },
                    new ModelFallbackTarget { ProviderId = "second", Model = "beta" },
                ],
            }
        );

        await harness.ConfigStore.SaveAsync(document);
        var reloaded = await harness.ConfigStore.LoadAsync();

        var provider = reloaded.Providers[ProviderId];
        Assert.True(provider.Enabled);
        Assert.Equal(ProviderCompatibility.Anthropic, provider.Compatibility);
        Assert.Equal(4, provider.MaxConcurrency);
        Assert.Equal(60, provider.RateLimit);
        Assert.Equal(30, provider.RateWindow);
        Assert.Equal(new[] { "alpha", "beta" }, provider.Models.ToArray());
        Assert.Equal("Demo", provider.Custom!.Label);
        Assert.True(provider.IsCustom);

        Assert.True(reloaded.Routing.Opus.IsActive);
        Assert.Equal("alpha", reloaded.Routing.Opus.Model);
        Assert.True(reloaded.TokenSavers.CavemanEnabled);
        Assert.Equal(CavemanLevel.Ultra, reloaded.TokenSavers.CavemanLevel);
        Assert.Equal(ModelMode.Chains, reloaded.ModelMode);
        Assert.Single(reloaded.ModelFallbacks);
        Assert.Equal(2, reloaded.ModelFallbacks[0].Models.Count);
    }

    [Fact]
    public async Task Load_Falls_Back_To_Defaults_For_Corrupt_Json()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.ConfigStore.LoadAsync();

        await using (var connection = await harness.ConnectionFactory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE app_state SET value = '{ not valid json' WHERE key = 'config';";
            await command.ExecuteNonQueryAsync();
        }

        var document = await harness.ConfigStore.LoadAsync();

        Assert.Equal(GatewayDefaults.ProxyPort, document.Server.ProxyPort);
    }

    [Fact]
    public async Task Load_Repairs_Null_Collections_From_Stored_Json()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.ConfigStore.LoadAsync();

        var sparse = JsonSerializer.Serialize(
            new { providers = (object?)null, modelFallbacks = (object?)null },
            GatewayJson.Options
        );

        await using (var connection = await harness.ConnectionFactory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE app_state SET value = $value WHERE key = 'config';";
            command.Parameters.AddWithValue("$value", sparse);
            await command.ExecuteNonQueryAsync();
        }

        var document = await harness.ConfigStore.LoadAsync();

        Assert.NotNull(document.Providers);
        Assert.NotNull(document.ModelFallbacks);
    }

    [Fact]
    public async Task Save_Rejects_Null_Document()
    {
        using var harness = await StorageHarness.CreateAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.ConfigStore.SaveAsync(null!));
    }

    private static async Task<string> ReadStoredConfigJsonAsync(StorageHarness harness)
    {
        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = 'config';";

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
