using Common;
using Common.AIProviders;
using Common.Routes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClaudeCodeGateway.Tests;

public class ClaudeGatewayOptionsValidationTests
{
    [Fact]
    public void ValidateOnStart_Fails_When_Providers_IsEmpty()
    {
        var services = new ServiceCollection();

        services
            .AddOptions<ClaudeGatewayOptions>()
            .Configure(o =>
            {
                o.GatewayBaseAddress = "http://localhost:4000";
                o.EnableTelemetry = true;
                o.Providers = new();
                o.RouteModel = new RouteModel
                {
                    DefaultModel = "azure/gpt-4.1",
                    ThinkingModel = "azure/gpt-4.1",
                    SearchModel = "azure/gpt-4.1",
                };
            })
            .Validate(
                opt => opt.Providers is { Count: > 0 },
                "At least one provider must be configured in 'Providers'."
            )
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<ClaudeGatewayOptions>>().Value
        );

        Assert.Contains("At least one provider must be configured", ex.Message);
    }

    [Fact]
    public void ValidateOnStart_Fails_When_RouteModel_IsMissing()
    {
        var services = new ServiceCollection();

        services
            .AddOptions<ClaudeGatewayOptions>()
            .Configure(o =>
            {
                o.GatewayBaseAddress = "http://localhost:4000";
                o.EnableTelemetry = true;
                o.Providers =
                [
                    new ClaudeGatewayProvider
                    {
                        ProviderType = ProviderType.Azure,
                        BaseAddress = "https://example/",
                        ApiKey = "k",
                        DeploymentName = "gpt-4.1",
                        Models = ["gpt-4.1"],
                    },
                ];
                o.RouteModel = null!;
            })
            .Validate(opt => opt.RouteModel is not null, "RouteModel must be configured.")
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<ClaudeGatewayOptions>>().Value
        );

        Assert.Contains("RouteModel must be configured", ex.Message);
    }

    [Fact]
    public void ValidateOnStart_Succeeds_When_Providers_HasAtLeastOne_And_RouteModel_IsConfigured()
    {
        var services = new ServiceCollection();

        services
            .AddOptions<ClaudeGatewayOptions>()
            .Configure(o =>
            {
                o.GatewayBaseAddress = "http://localhost:4000";
                o.EnableTelemetry = true;
                o.Providers =
                [
                    new ClaudeGatewayProvider
                    {
                        ProviderType = ProviderType.Azure,
                        BaseAddress = "https://example/",
                        ApiKey = "k",
                        DeploymentName = "gpt-4.1",
                        Models = ["gpt-4.1"],
                    },
                ];
                o.RouteModel = new RouteModel
                {
                    DefaultModel = "azure/gpt-4.1",
                    ThinkingModel = "azure/gpt-4.1",
                    SearchModel = "azure/gpt-4.1",
                };
            })
            .Validate(
                opt => opt.Providers is { Count: > 0 },
                "At least one provider must be configured in 'Providers'."
            )
            .Validate(opt => !string.IsNullOrWhiteSpace(opt.RouteModel.DefaultModel), "RouteModel.DefaultModel must be configured.")
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<ClaudeGatewayOptions>>().Value;
        Assert.Single(opts.Providers);
    }
}
