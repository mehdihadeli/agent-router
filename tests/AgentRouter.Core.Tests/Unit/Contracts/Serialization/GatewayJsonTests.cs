using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Serialization;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Contracts.Serialization;

public sealed class GatewayJsonTests
{
    [Theory]
    [InlineData("api_key", "ApiKey")]
    [InlineData("oauth", "OAuth")]
    public void Credentials_ReadLegacyNamesAndWritePascalCase(string legacy, string expected)
    {
        var value = JsonSerializer.Deserialize<ProviderAuthType>(
            JsonSerializer.Serialize(legacy),
            GatewayJson.Options
        );
        Assert.Equal(
            JsonSerializer.Serialize(expected),
            JsonSerializer.Serialize(value, GatewayJson.Options)
        );
    }

    [Fact]
    public void Routing_ReadsLegacyStrategyAndWritesPascalCase()
    {
        var value = JsonSerializer.Deserialize<RoutingStrategy>(
            "\"round_robin\"",
            GatewayJson.Options
        );
        Assert.Equal(RoutingStrategy.RoundRobin, value);
        Assert.Equal("\"RoundRobin\"", JsonSerializer.Serialize(value, GatewayJson.Options));
    }

    [Fact]
    public void Settings_WritePascalCaseWithoutChangingUserIdentifiers()
    {
        var settings = new GatewaySettings
        {
            Providers = new()
            {
                ["work-provider"] = new()
                {
                    Protocol = ProviderCompatibility.OpenAi,
                    Models = ["owner/native-model"],
                },
            },
        };
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(settings, GatewayJson.Indented)
        );
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("schemaVersion", out _));
        var provider = json.RootElement.GetProperty("Providers").GetProperty("work-provider");
        Assert.Equal("OpenAi", provider.GetProperty("Protocol").GetString());
        Assert.Equal("owner/native-model", provider.GetProperty("Models")[0].GetString());
    }

    [Theory]
    [InlineData("SchemaVersion", "Server", "Port")]
    [InlineData("schemaVersion", "server", "port")]
    public void Settings_ReadPascalCaseAndLegacyCamelCase(string schema, string server, string port)
    {
        var json = JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                [schema] = 1,
                [server] = new Dictionary<string, int> { [port] = 4600 },
            }
        );
        var settings = JsonSerializer.Deserialize<GatewaySettings>(json, GatewayJson.Options);
        Assert.NotNull(settings);
        Assert.Equal(1, settings.SchemaVersion);
        Assert.Equal(4600, settings.Server.Port);
    }
}
