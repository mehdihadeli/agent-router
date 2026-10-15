using AgentRouter.Core.Constants;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Providers.Anthropic;
using AgentRouter.Core.Providers.Azure;
using AgentRouter.Core.Providers.DeepSeek;
using AgentRouter.Core.Providers.OpenAI;
using AgentRouter.Core.Routes;
using Microsoft.Extensions.AI;

namespace AgentRouter.Core.Providers;

public static class ClientExtensions
{
    private const string GitHubModelsDefaultEndpoint = "https://models.github.ai/inference/";

    public static IHostApplicationBuilder AddAIProviderChatClient(
        this IHostApplicationBuilder builder,
        ClaudeGatewayOptions? runtimeOptions = null
    )
    {
        // Bind from root because the user config is flat (GatewayBaseAddress, Providers, RouteModel, ...)
        // and is loaded into the root configuration by our configuration providers.
        var options = new ClaudeGatewayOptions();
        builder.Configuration.Bind(options);
        options = runtimeOptions ?? options;

        ArgumentNullException.ThrowIfNull(options.RouteModel);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxProviderAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxProviderAttempts, 8);
        GatewayResilience.Register(builder.Services, options);

        // Bind ClaudeGatewayOptions from the flat root keys loaded from ~/agent-router/config.json.
        // Our user config is flat (GatewayBaseAddress, DefaultProviderType, DefaultModel, Providers, ...),
        // so we can bind from the root configuration.
        builder
            .Services.AddOptions<ClaudeGatewayOptions>()
            .Configure<IConfiguration>(
                (opt, cfg) =>
                {
                    if (runtimeOptions is null)
                    {
                        cfg.Bind(opt);
                    }
                    else
                    {
                        opt.Providers = runtimeOptions.Providers;
                        opt.RouteModel = runtimeOptions.RouteModel;
                        opt.TierRoutes = runtimeOptions.TierRoutes;
                        opt.GatewayBaseAddress = runtimeOptions.GatewayBaseAddress;
                        opt.EnableTelemetry = runtimeOptions.EnableTelemetry;
                        opt.RequestTimeoutSeconds = runtimeOptions.RequestTimeoutSeconds;
                        opt.MaxProviderAttempts = runtimeOptions.MaxProviderAttempts;
                        opt.RoutingSettings = runtimeOptions.RoutingSettings;
                        opt.GitBashPath = runtimeOptions.GitBashPath;
                    }
                }
            )
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register one keyed `IChatClient` per configured provider.
        // The key is the provider's logical name (ProviderName) when present, otherwise ProviderType (e.g. "azure").
        foreach (var provider in options.Providers)
        {
            if (
                provider.ProviderType
                is not (
                    ProviderType.OpenAI
                    or ProviderType.Azure
                    or ProviderType.GitHubModels
                    or ProviderType.Anthropic
                )
            )
                throw new NotSupportedException(
                    $"Provider type '{provider.ProviderType}' is not supported."
                );
            foreach (var providerModel in provider.Models)
            {
                // like `azure/gpt-4.1`, `azure/gpt-4`
                var providerModelKey = RouteModelFactory.GetProviderModelName(
                    provider,
                    providerModel
                );

                switch (provider.ProviderType)
                {
                    case ProviderType.Anthropic:
                        AnthropicProvider.Register(
                            builder,
                            provider,
                            providerModel,
                            providerModelKey,
                            options.EnableTelemetry,
                            1
                        );
                        break;
                    case ProviderType.Azure:
                        RegisterAzureAI(
                            builder,
                            baseAddress: provider.BaseAddress,
                            apiKey: provider.ApiKey,
                            deploymentName: providerModel,
                            model: providerModel,
                            enableTelemetry: options.EnableTelemetry,
                            providerModelKey,
                            maxProviderAttempts: 1
                        );
                        break;

                    case ProviderType.OpenAI:
                        RegisterOpenAI(
                            builder,
                            baseAddress: provider.BaseAddress,
                            apiKey: provider.ApiKey,
                            deploymentName: providerModel,
                            model: providerModel,
                            enableTelemetry: options.EnableTelemetry,
                            providerModelKey,
                            maxProviderAttempts: 1
                        );
                        break;

                    case ProviderType.GitHubModels:
                        RegisterGitHubModels(
                            builder,
                            baseAddress: provider.BaseAddress,
                            apiKey: provider.ApiKey,
                            model: providerModel,
                            enableTelemetry: options.EnableTelemetry,
                            providerModelKey,
                            maxProviderAttempts: 1
                        );
                        break;
                }
            }
        }

        // register rout model providers
        RegisterRoute(
            builder.Services,
            options,
            RouteModelKeys.Default,
            options.RouteModel.DefaultModel
        );
        RegisterRoute(
            builder.Services,
            options,
            RouteModelKeys.Thinking,
            options.RouteModel.ThinkingModel
        );
        RegisterRoute(
            builder.Services,
            options,
            RouteModelKeys.Search,
            options.RouteModel.SearchModel
        );
        builder.Services.AddSingleton(services =>
            services.GetRequiredKeyedService<IChatClient>(RouteModelKeys.Default)
        );

        return builder;
    }

    private static void RegisterRoute(
        IServiceCollection services,
        ClaudeGatewayOptions options,
        string key,
        string route
    )
    {
        var resolved = RouteModelFactory.Resolve(options, route);
        var providerKey = RouteModelFactory.GetProviderModelName(resolved.Provider, resolved.Model);
        services.AddKeyedSingleton<IChatClient>(
            key,
            (provider, _) => provider.GetRequiredKeyedService<IChatClient>(providerKey)
        );
    }

    private static void RegisterGitHubModels(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null,
        int maxProviderAttempts = 1
    )
    {
        var endpoint = string.IsNullOrWhiteSpace(baseAddress)
            ? GitHubModelsDefaultEndpoint
            : baseAddress;

        // GitHub Models is OpenAI-compatible; model id is used as deploymentName.
        RegisterOpenAI(
            builder,
            baseAddress: endpoint,
            apiKey: apiKey,
            deploymentName: model,
            model: model,
            enableTelemetry: enableTelemetry,
            providerModelKey: providerModelKey,
            maxProviderAttempts: maxProviderAttempts
        );
    }

    private static void RegisterAzureAI(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string deploymentName,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null,
        int maxProviderAttempts = 1
    )
    {
        // https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-AzureOpenAI%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp
        ArgumentException.ThrowIfNullOrEmpty(baseAddress);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(deploymentName);

        var azureOpenAIApiClientBuilder = builder.AddAzureOpenAIClient(
            new AzureOpenAIApiClientSettings
            {
                ApiKey = apiKey,
                Endpoint = baseAddress,
                DisableTracing = !enableTelemetry,
            },
            // register with a keyed service provider with `ProviderType` as a key
            serviceKey: providerModelKey,
            maxProviderAttempts: maxProviderAttempts
        );

        // https://learn.microsoft.com/en-us/dotnet/ai/dotnet-ai-ecosystem#semantic-kernel-for-net
        // Register `IChatClient` which is based on `Microsoft.Extensions.AI` abstractions and implemented by semantic kernel connectors. To use the same client abstractions across different AI frameworks.
        azureOpenAIApiClientBuilder.AddAzureOpenAIChatClient(
            deploymentName: deploymentName,
            model: model,
            configureOpenTelemetry: otel =>
                otel.EnableSensitiveData = builder.Configuration.GetValue<bool>(
                    "Telemetry:EnableSensitiveData"
                )
        );
    }

    private static void RegisterOpenAI(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string deploymentName,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null,
        int maxProviderAttempts = 1
    )
    {
        // https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-OpenAI%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(deploymentName);

        var openAiApiClientBuilder = builder.AddOpenAIClient(
            new OpenAIApiClientSettings
            {
                ApiKey = apiKey,
                Endpoint = baseAddress,
                DisableTracing = !enableTelemetry,
                ConfigureClient = DeepSeekToolPolicy.ConfigureClient,
            },
            // register with a keyed service provider with `ProviderType` as a key
            serviceKey: providerModelKey,
            maxProviderAttempts: maxProviderAttempts
        );

        // https://learn.microsoft.com/en-us/dotnet/ai/dotnet-ai-ecosystem#semantic-kernel-for-net
        // Register `IChatClient` which is based on `Microsoft.Extensions.AI` abstractions and implemented by semantic kernel connectors. To use the same client abstractions across different AI frameworks.
        openAiApiClientBuilder.AddOpenAIChatClient(
            deploymentName: deploymentName,
            model: model,
            configureOpenTelemetry: otel =>
                otel.EnableSensitiveData = builder.Configuration.GetValue<bool>(
                    "Telemetry:EnableSensitiveData"
                )
        );
    }
}
