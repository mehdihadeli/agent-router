using System.Text.Json;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Sqlite;

namespace AgentRouter.Core.Hosting;

public static class GatewayRuntimeConfiguration
{
    public static GatewayConfigDocument ResolveSettings(
        GatewaySettings settings,
        IConfiguration configuration
    )
    {
        GatewaySettingsLoader.Validate(settings);
        var token = settings.Server.TokenEnvironment is null
            ? null
            : configuration[settings.Server.TokenEnvironment];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "Runtime settings require server.tokenEnvironment naming a nonempty gateway token environment variable."
            );
        var document = new GatewayConfigDocument();
        document.Server.ProxyPort = settings.Server.Port;
        document.Server.AuthToken = token;
        foreach (var (id, provider) in settings.Providers.Where(entry => entry.Value.Enabled))
        {
            var credentials = provider
                .Credentials.Where(credential => credential.Enabled)
                .ToArray();
            if (credentials.Length != 1 || credentials[0].Environment is null)
                throw new InvalidOperationException(
                    "Runtime settings currently require one enabled environment credential per provider; secret references and credential rotation are not supported."
                );
            var key = configuration[credentials[0].Environment!];
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException(
                    $"Provider '{id}' credential environment variable is empty."
                );
            document.Providers[id] = new ProviderConfig
            {
                Enabled = true,
                BaseUrl = provider.Endpoint,
                ApiKey = key,
                Models = provider.Models.ToList(),
                Compatibility = provider.Protocol,
            };
        }
        var (defaultProvider, defaultModel) = GatewaySettingsLoader.ValidateModel(
            settings,
            settings.Routing.DefaultModel
        );
        document.Routing.Default = new RoutingRule
        {
            Enabled = true,
            ProviderId = defaultProvider,
            Model = defaultModel,
        };
        foreach (var (name, agent) in settings.Agents)
            document.Profiles.Add(
                new AgentProfile
                {
                    Name = name,
                    Kind = agent.Kind,
                    Model = agent.Model,
                    Executable = agent.Executable ?? agent.Kind,
                }
            );
        return document;
    }

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
        var document = provider
            .GetRequiredService<IConfigStore>()
            .LoadAsync()
            .GetAwaiter()
            .GetResult();
        return document.Providers.Count == 0 ? null : document;
    }

    public static GatewayConfigDocument ApplyOverrides(
        GatewayConfigDocument document,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);
        var sections = typeof(GatewayConfigDocument)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyProviders = configuration
            .GetSection("Providers")
            .GetChildren()
            .Where(section => section["ProviderType"] is not null)
            .Select(section => section.Path)
            .ToArray();
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document));
        using var effective = (ConfigurationRoot)
            new ConfigurationBuilder()
                .AddJsonStream(stream)
                .AddInMemoryCollection(
                    configuration
                        .AsEnumerable()
                        .Where(entry =>
                            sections.Contains(entry.Key.Split(':', 2)[0])
                            && !legacyProviders.Any(path =>
                                entry.Key.Equals(path, StringComparison.OrdinalIgnoreCase)
                                || entry.Key.StartsWith(
                                    path + ":",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        )
                )
                .Build();
        return effective.Get<GatewayConfigDocument>()
            ?? throw new InvalidOperationException("Cannot bind effective gateway configuration.");
    }

    public static ClaudeGatewayOptions Project(
        GatewayConfigDocument document,
        IConfiguration configuration
    ) => Project(document, configuration, applyOverrides: true);

    internal static ClaudeGatewayOptions Project(
        GatewayConfigDocument document,
        IConfiguration configuration,
        bool applyOverrides
    )
    {
        if (applyOverrides)
            document = ApplyOverrides(document, configuration);
        if (document.Server.ProxyPort is < 1 or > 65535)
            throw new InvalidOperationException("Proxy port must be between 1 and 65535.");
        var options = new ClaudeGatewayOptions();
        configuration.Bind(options);
        options.Providers = [];
        options.TierRoutes = new(StringComparer.OrdinalIgnoreCase);
        options.GatewayBaseAddress =
            $"http://{GatewayDefaults.LoopbackHost}:{document.Server.ProxyPort}";

        foreach (var (id, provider) in document.Providers.Where(entry => entry.Value.Enabled))
        {
            if (
                provider.Compatibility
                    is not (ProviderCompatibility.OpenAi or ProviderCompatibility.Anthropic)
                || provider.AuthType != ProviderAuthType.ApiKey
            )
            {
                throw new NotSupportedException(
                    $"Provider '{id}' requires an unsupported protocol or authentication type. Use OpenAI or Anthropic API-key providers."
                );
            }
            if (
                !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query)
                || !string.IsNullOrEmpty(endpoint.Fragment)
                || string.IsNullOrWhiteSpace(provider.ApiKey)
            )
            {
                throw new InvalidOperationException(
                    $"Provider '{id}' needs an HTTP(S) base URL and API key."
                );
            }

            options.Providers.Add(
                new ClaudeGatewayProvider
                {
                    ProviderName = id,
                    ProviderType =
                        provider.Compatibility == ProviderCompatibility.Anthropic
                            ? ProviderType.Anthropic
                        : id.Equals("azure", StringComparison.OrdinalIgnoreCase)
                            ? ProviderType.Azure
                        : id.Equals("github", StringComparison.OrdinalIgnoreCase)
                            ? ProviderType.GitHubModels
                        : ProviderType.OpenAI,
                    BaseAddress = endpoint.AbsoluteUri,
                    ApiKey = provider.ApiKey,
                    Models = provider
                        .Models.Where(model =>
                            !provider.DisabledModels.Contains(
                                model,
                                StringComparer.OrdinalIgnoreCase
                            )
                        )
                        .ToArray(),
                }
            );
        }

        var active =
            options.Providers.FirstOrDefault(provider =>
                string.Equals(
                    provider.ProviderName,
                    document.ActiveProvider,
                    StringComparison.OrdinalIgnoreCase
                )
            ) ?? options.Providers.FirstOrDefault();
        if (active is null || active.Models.Length == 0)
        {
            throw new InvalidOperationException(
                "Configure an enabled provider with at least one enabled model. Run 'provider add' first."
            );
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
        foreach (
            var (tier, rule) in document
                .Routing.AsMap()
                .Where(entry => entry.Key != RoutingTier.Default && entry.Value.IsActive)
        )
        {
            var route = $"{rule.ProviderId}/{rule.Model}";
            RouteModelFactory.Resolve(options, route);
            options.TierRoutes[tier.ToString().ToLowerInvariant()] = route;
        }

        return options;
    }
}
