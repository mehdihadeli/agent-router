using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Defaults;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using AgentRouter.Storage;
using AgentRouter.Storage.Configuration;
using AgentRouter.Storage.Sqlite;

namespace AgentRouter;

public static class GatewayRuntimeConfiguration
{
    public static GatewayConfigDocument? Load()
    {
        if (!File.Exists(GatewayPaths.Resolve().DatabasePath))
        {
            return null;
        }

        var services = new ServiceCollection();
        services.AddGatewayStorage();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GatewayDatabase>().InitializeAsync().GetAwaiter().GetResult();
        var document = provider.GetRequiredService<IConfigStore>().LoadAsync().GetAwaiter().GetResult();
        return document.Providers.Count == 0 ? null : document;
    }

    public static ClaudeGatewayOptions Project(GatewayConfigDocument document, IConfiguration configuration)
    {
        if (document.Server.ProxyPort is < 1 or > 65535)
            throw new InvalidOperationException("Proxy port must be between 1 and 65535.");
        var options = new ClaudeGatewayOptions();
        configuration.Bind(options);
        options.Providers = [];
        options.TierRoutes = new(StringComparer.OrdinalIgnoreCase);
        options.GatewayBaseAddress = $"http://{GatewayDefaults.LoopbackHost}:{document.Server.ProxyPort}";

        foreach (var (id, provider) in document.Providers.Where(entry => entry.Value.Enabled))
        {
            if (provider.Compatibility is not (ProviderCompatibility.OpenAi or ProviderCompatibility.Anthropic)
                || provider.AuthType != ProviderAuthType.ApiKey)
            {
                throw new NotSupportedException($"Provider '{id}' requires an unsupported protocol or authentication type. Use OpenAI or Anthropic API-key providers.");
            }
            if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query)
                || !string.IsNullOrEmpty(endpoint.Fragment)
                || string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                throw new InvalidOperationException($"Provider '{id}' needs an HTTP(S) base URL and API key.");
            }

            options.Providers.Add(new ClaudeGatewayProvider
            {
                ProviderName = id,
                ProviderType = provider.Compatibility == ProviderCompatibility.Anthropic
                    ? Shared.AIProviders.ProviderType.Anthropic
                    : id.Equals("azure", StringComparison.OrdinalIgnoreCase)
                    ? Shared.AIProviders.ProviderType.Azure
                    : id.Equals("github", StringComparison.OrdinalIgnoreCase)
                        ? Shared.AIProviders.ProviderType.GitHubModels
                        : Shared.AIProviders.ProviderType.OpenAI,
                BaseAddress = endpoint.AbsoluteUri,
                ApiKey = provider.ApiKey,
                Models = provider.Models.Where(model => !provider.DisabledModels.Contains(model, StringComparer.OrdinalIgnoreCase)).ToArray(),
            });
        }

        var active = options.Providers.FirstOrDefault(provider => string.Equals(provider.ProviderName, document.ActiveProvider, StringComparison.OrdinalIgnoreCase))
            ?? options.Providers.FirstOrDefault();
        if (active is null || active.Models.Length == 0)
        {
            throw new InvalidOperationException("Configure an enabled provider with at least one enabled model. Run 'provider add' first.");
        }

        var defaultRoute = document.Routing.Default.IsActive
            ? $"{document.Routing.Default.ProviderId}/{document.Routing.Default.Model}"
            : RouteModelFactory.GetProviderModelName(active, active.Models[0]);
        options.RouteModel = new RouteModel
        {
            DefaultModel = defaultRoute,
            ThinkingModel = defaultRoute,
            SearchModel = defaultRoute,
        };
        RouteModelFactory.Resolve(options, defaultRoute);
        foreach (var (tier, rule) in document.Routing.AsMap().Where(entry => entry.Key != RoutingTier.Default && entry.Value.IsActive))
        {
            var route = $"{rule.ProviderId}/{rule.Model}";
            RouteModelFactory.Resolve(options, route);
            options.TierRoutes[tier.ToString().ToLowerInvariant()] = route;
        }

        return options;
    }
}
