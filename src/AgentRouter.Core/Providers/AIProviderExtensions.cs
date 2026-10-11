using AgentRouter.Shared.AIProviders.Azure;
using AgentRouter.Shared.AIProviders.OpenAI;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Constants;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.AI;
using Anthropic;
using Anthropic.Core;

namespace AgentRouter.Shared.AIProviders;

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
            if (provider.ProviderType is not (ProviderType.OpenAI or ProviderType.Azure
                or ProviderType.GitHubModels or ProviderType.Anthropic))
                throw new NotSupportedException($"Provider type '{provider.ProviderType}' is not supported.");
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
                        var pipeline = builder.Services.AddKeyedChatClient(providerModelKey, _ =>
                            new AnthropicClient(new ClientOptions
                            {
                                ApiKey = provider.ApiKey,
                                BaseUrl = provider.BaseAddress,
                                MaxRetries = 0,
                            }).AsIChatClient(defaultModelId: providerModel, defaultMaxOutputTokens: 1024));
                        if (options.EnableTelemetry)
                            pipeline.UseOpenTelemetry(sourceName: "Experimental.Microsoft.Extensions.AI", configure: telemetry =>
                                telemetry.EnableSensitiveData = builder.Configuration.GetValue<bool>("Telemetry:EnableSensitiveData"));
                        pipeline.UseLogging();
                        break;
                    case ProviderType.Azure:
                        RegisterAzureAI(
                            builder,
                            baseAddress: provider.BaseAddress,
                            apiKey: provider.ApiKey,
                            deploymentName: providerModel,
                            model: providerModel,
                            enableTelemetry: options.EnableTelemetry,
                            providerModelKey
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
                            providerModelKey
                        );
                        break;

                    case ProviderType.GitHubModels:
                        RegisterGitHubModels(
                            builder,
                            baseAddress: provider.BaseAddress,
                            apiKey: provider.ApiKey,
                            model: providerModel,
                            enableTelemetry: options.EnableTelemetry,
                            providerModelKey
                        );
                        break;
                }
            }
        }

        // register rout model providers
        RegisterRoute(builder.Services, options, RouteModelKeys.Default, options.RouteModel.DefaultModel);
        RegisterRoute(builder.Services, options, RouteModelKeys.Thinking, options.RouteModel.ThinkingModel);
        RegisterRoute(builder.Services, options, RouteModelKeys.Search, options.RouteModel.SearchModel);
        builder.Services.AddSingleton(services => services.GetRequiredKeyedService<IChatClient>(RouteModelKeys.Default));

        return builder;
    }

    private static void RegisterRoute(IServiceCollection services, ClaudeGatewayOptions options, string key, string route)
    {
        var resolved = RouteModelFactory.Resolve(options, route);
        var providerKey = RouteModelFactory.GetProviderModelName(resolved.Provider, resolved.Model);
        services.AddKeyedSingleton<IChatClient>(key,
            (provider, _) => provider.GetRequiredKeyedService<IChatClient>(providerKey));
    }

    private static void RegisterGitHubModels(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null
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
            providerModelKey: providerModelKey
        );
    }

    private static void RegisterAzureAI(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string deploymentName,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null
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
            serviceKey: providerModelKey
        );

        // https://learn.microsoft.com/en-us/dotnet/ai/dotnet-ai-ecosystem#semantic-kernel-for-net
        // Register `IChatClient` which is based on `Microsoft.Extensions.AI` abstractions and implemented by semantic kernel connectors. To use the same client abstractions across different AI frameworks.
        azureOpenAIApiClientBuilder.AddAzureOpenAIChatClient(
            deploymentName: deploymentName,
            model: model,
            configureOpenTelemetry: otel =>
                otel.EnableSensitiveData = builder.Configuration.GetValue<bool>("Telemetry:EnableSensitiveData")
        );
    }

    private static void RegisterOpenAI(
        IHostApplicationBuilder builder,
        string baseAddress,
        string apiKey,
        string deploymentName,
        string model,
        bool enableTelemetry,
        object? providerModelKey = null
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
            },
            // register with a keyed service provider with `ProviderType` as a key
            serviceKey: providerModelKey
        );

        // https://learn.microsoft.com/en-us/dotnet/ai/dotnet-ai-ecosystem#semantic-kernel-for-net
        // Register `IChatClient` which is based on `Microsoft.Extensions.AI` abstractions and implemented by semantic kernel connectors. To use the same client abstractions across different AI frameworks.
        openAiApiClientBuilder.AddOpenAIChatClient(
            deploymentName: deploymentName,
            model: model,
            configureOpenTelemetry: otel =>
                otel.EnableSensitiveData = builder.Configuration.GetValue<bool>("Telemetry:EnableSensitiveData")
        );
    }
}
