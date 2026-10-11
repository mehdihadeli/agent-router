using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentRouter.Tests;

public class ClaudeGatewayOptionsBindingTests
{
    [Fact]
    public void Binds_ClaudeGatewayOptions_FromFlatConfigRoot()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "ccg-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempHome, "claude-code-gateway"));

        Environment.SetEnvironmentVariable("TEST_BASE", "https://example.test/");
        Environment.SetEnvironmentVariable("TEST_KEY", "k123");
        try
        {
            File.WriteAllText(
                Path.Combine(tempHome, "claude-code-gateway", "config.json"),
                "{\"GatewayBaseAddress\":\"http://localhost:4000\",\"EnableTelemetry\":true,\"RouteModel\":{\"DefaultModel\":\"azure/gpt-4.1\",\"ThinkingModel\":\"azure/gpt-4.1\",\"SearchModel\":\"azure/gpt-4.1\"},\"Providers\":[{\"ProviderType\":\"Azure\",\"BaseAddress\":\"${TEST_BASE}\",\"ApiKey\":\"${TEST_KEY}\",\"DeploymentName\":\"gpt-4.1\",\"Models\":[\"gpt-4.1\"]}]}"
            );

            var configuration = new ConfigurationBuilder()
                .Add(
                    new AgentRouterConfigConfigurationSource
                    {
                        HomeOverridePath = tempHome,
                        CreateIfMissing = false,
                    }
                )
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services
                .AddOptions<ClaudeGatewayOptions>()
                .Configure<IConfiguration>((opt, cfg) => cfg.Bind(opt));
            services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<ClaudeGatewayOptions>>().Value
            );

            var provider = services.BuildServiceProvider();
            var bound = provider.GetRequiredService<ClaudeGatewayOptions>();

            Assert.Equal("http://localhost:4000", bound.GatewayBaseAddress);
            Assert.True(bound.EnableTelemetry);

            Assert.NotNull(bound.RouteModel);
            Assert.Equal("azure/gpt-4.1", bound.RouteModel.DefaultModel);
            Assert.Equal("azure/gpt-4.1", bound.RouteModel.ThinkingModel);
            Assert.Equal("azure/gpt-4.1", bound.RouteModel.SearchModel);

            Assert.Single(bound.Providers);
            Assert.Equal(ProviderType.Azure, bound.Providers[0].ProviderType);
            Assert.Equal("https://example.test/", bound.Providers[0].BaseAddress);
            Assert.Equal("k123", bound.Providers[0].ApiKey);
            Assert.Equal("gpt-4.1", bound.Providers[0].DeploymentName);
            Assert.Equal(new[] { "gpt-4.1" }, bound.Providers[0].Models);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_BASE", null);
            Environment.SetEnvironmentVariable("TEST_KEY", null);
        }
    }
}
