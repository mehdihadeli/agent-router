using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgentRouter.Providers.OpenAI.Tests;

public class ProviderRegistrationTests
{
    [Fact]
    public void Provider_registration_surface_is_available()
    {
        Assert.NotNull(typeof(ClientExtensions));
    }

    [Fact]
    public void NamedProvidersAndRouteAliases_ResolveCorrectClient()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddAIProviderChatClient(new ClaudeGatewayOptions
        {
            EnableTelemetry = false,
            Providers =
            [
                new ClaudeGatewayProvider { ProviderName = "first", ProviderType = ProviderType.OpenAI, BaseAddress = "https://first.test/v1", ApiKey = "first-key", Models = ["shared"] },
                new ClaudeGatewayProvider { ProviderName = "second", ProviderType = ProviderType.OpenAI, BaseAddress = "https://second.test/v1", ApiKey = "second-key", Models = ["shared"] },
                new ClaudeGatewayProvider { ProviderName = "claude", ProviderType = ProviderType.Anthropic, BaseAddress = "https://anthropic.test", ApiKey = "anthropic-key", Models = ["native"] },
            ],
            RouteModel = new RouteModel { DefaultModel = "second/shared", ThinkingModel = "claude/native", SearchModel = "first/shared" },
        });
        using var host = builder.Build();
        var first = host.Services.GetRequiredKeyedService<IChatClient>("first/shared");
        var second = host.Services.GetRequiredKeyedService<IChatClient>("second/shared");
        var anthropic = host.Services.GetRequiredKeyedService<IChatClient>("claude/native");
        Assert.NotSame(first, second);
        Assert.Same(second, host.Services.GetRequiredKeyedService<IChatClient>("default"));
        Assert.Same(second, host.Services.GetRequiredService<IChatClient>());
        Assert.Same(anthropic, host.Services.GetRequiredKeyedService<IChatClient>("thinking"));
        Assert.Same(first, host.Services.GetRequiredKeyedService<IChatClient>("search"));
    }

    [Fact]
    public void UnsupportedProviderType_FailsAtRegistration()
    {
        var builder = Host.CreateApplicationBuilder();
        Assert.Throws<NotSupportedException>(() => builder.AddAIProviderChatClient(new ClaudeGatewayOptions
        {
            Providers = [new ClaudeGatewayProvider { ProviderType = ProviderType.Ollama, BaseAddress = "http://localhost:11434", ApiKey = "key", Models = ["model"] }],
            RouteModel = new RouteModel { DefaultModel = "ollama/model", ThinkingModel = "ollama/model", SearchModel = "ollama/model" },
        }));
    }
}
