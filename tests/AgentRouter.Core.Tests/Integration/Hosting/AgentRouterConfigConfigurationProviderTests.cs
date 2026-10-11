using AgentRouter.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentRouter.Tests;

public class AgentRouterConfigConfigurationProviderTests
{
    [Fact]
    public void FlatSchema_IsMappedInto_ClaudeGatewayOptionsSection()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(tempHome, "claude-code-gateway");
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "config.json"),
            "{\"GatewayBaseAddress\":\"http://localhost:4000\",\"DefaultProviderType\":\"Azure\"}"
        );

        var config = new ConfigurationBuilder()
            .Add(
                new AgentRouterConfigConfigurationSource
                {
                    HomeOverridePath = tempHome,
                    CreateIfMissing = false,
                }
            )
            .Build();

        Assert.Equal("http://localhost:4000", config["GatewayBaseAddress"]);
        Assert.Equal("Azure", config["DefaultProviderType"]);
    }

    [Fact]
    public void FlatSchema_Interpolates_EnvironmentVariables()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(tempHome, "claude-code-gateway");
        Directory.CreateDirectory(folder);

        Environment.SetEnvironmentVariable("GW", "http://env-host:1234");
        try
        {
            File.WriteAllText(
                Path.Combine(folder, "config.json"),
                "{\"GatewayBaseAddress\":\"$GW\",\"DefaultProviderType\":\"Azure\"}"
            );

            var config = new ConfigurationBuilder()
                .Add(
                    new AgentRouterConfigConfigurationSource
                    {
                        HomeOverridePath = tempHome,
                        CreateIfMissing = false,
                    }
                )
                .Build();

            Assert.Equal("http://env-host:1234", config["GatewayBaseAddress"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW", null);
        }
    }
}
