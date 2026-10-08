using ClaudeCodeGateway.Shared.AIProviders.Azure;
using ClaudeCodeGateway.Shared.AIProviders.OpenAI;
using ClaudeCodeGateway.Shared.Configuration;
using ClaudeCodeGateway.Shared.Constants;
using ClaudeCodeGateway.Shared.Contracts;
using ClaudeCodeGateway.Shared.Routes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaudeCodeGateway.Shared.AIProviders;

public static class ClientExtensions
{
    private const string GitHubModelsDefaultEndpoint = "https://models.github.ai/inference/";

    public static IHostApplicationBuilder AddAIProviderChatClient(
        this IHostApplicationBuilder builder
    )
    {
        // Bind from root because the user config is flat (GatewayBaseAddress, Providers, RouteModel, ...)
        // and is loaded into the root configuration by our configuration providers.
        var options = new ClaudeGatewayOptions();
        builder.Configuration.Bind(options);

        ArgumentNullException.ThrowIfNull(options.RouteModel);

        // Bind ClaudeGatewayOptions from the flat root keys loaded from ~/claude-code-gateway/config.json.
        // Our user config is flat (GatewayBaseAddress, DefaultProviderType, DefaultModel, Providers, ...),
        // so we can bind from the root configuration.
        builder
            .Services.AddOptions<ClaudeGatewayOptions>()
            .Configure<IConfiguration>(
                (opt, cfg) =>
                {
                    cfg.Bind(opt);
                }
            )
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register one keyed `IChatClient` per configured provider.
        // The key is the provider's logical name (ProviderName) when present, otherwise ProviderType (e.g. "azure").
        foreach (var provider in options.Providers)
        {
            foreach (var providerModel in provider.Models)
            {
                // like `azure/gpt-4.1`, `azure/gpt-4`
                var providerModelKey = RouteModelFactory.GetProviderModelName(
                    provider.ProviderType,
                    providerModel
                );

                switch (provider.ProviderType)
                {
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
        RegisterDefaultRouteModel(builder, options);
        RegisterThinkingRouteModel(builder, options);
        RegisterSearchRouteModel(builder, options);

        return builder;
    }

    private static void RegisterSearchRouteModel(
        IHostApplicationBuilder builder,
        ClaudeGatewayOptions options
    )
    {
        var searchRouteModel =
            RouteModelFactory.Resolve(options, options.RouteModel.SearchModel)
            ?? throw new InvalidOperationException(
                "No search model route found, configure it in `RouteModel.Search`"
            );

        switch (searchRouteModel.ProviderType)
        {
            case ProviderType.Azure:
                RegisterAzureAI(
                    builder,
                    baseAddress: searchRouteModel.Provider.BaseAddress,
                    apiKey: searchRouteModel.Provider.ApiKey,
                    deploymentName: searchRouteModel.Model,
                    model: searchRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Search
                );
                break;

            case ProviderType.OpenAI:

                RegisterOpenAI(
                    builder,
                    baseAddress: searchRouteModel.Provider.BaseAddress,
                    apiKey: searchRouteModel.Provider.ApiKey,
                    deploymentName: searchRouteModel.Model,
                    model: searchRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Search
                );
                break;

            case ProviderType.GitHubModels:
                RegisterGitHubModels(
                    builder,
                    baseAddress: searchRouteModel.Provider.BaseAddress,
                    apiKey: searchRouteModel.Provider.ApiKey,
                    model: searchRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Search
                );
                break;
        }
    }

    private static void RegisterDefaultRouteModel(
        IHostApplicationBuilder builder,
        ClaudeGatewayOptions options
    )
    {
        var defaultRouteModel =
            RouteModelFactory.Resolve(options, options.RouteModel.DefaultModel)
            ?? throw new InvalidOperationException(
                "No default route model found, configure it in `RouteModel.Default`"
            );

        switch (defaultRouteModel.ProviderType)
        {
            case ProviderType.Azure:
                RegisterAzureAI(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    deploymentName: defaultRouteModel.Model,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry
                );
                RegisterAzureAI(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    deploymentName: defaultRouteModel.Model,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Default
                );
                break;

            case ProviderType.OpenAI:
                RegisterOpenAI(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    deploymentName: defaultRouteModel.Model,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry
                );
                RegisterOpenAI(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    deploymentName: defaultRouteModel.Model,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Default
                );
                break;

            case ProviderType.GitHubModels:
                RegisterGitHubModels(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry
                );
                RegisterGitHubModels(
                    builder,
                    baseAddress: defaultRouteModel.Provider.BaseAddress,
                    apiKey: defaultRouteModel.Provider.ApiKey,
                    model: defaultRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Default
                );
                break;
        }
    }

    private static void RegisterThinkingRouteModel(
        IHostApplicationBuilder builder,
        ClaudeGatewayOptions options
    )
    {
        var thinkingRouteModel =
            RouteModelFactory.Resolve(options, options.RouteModel.ThinkingModel)
            ?? throw new InvalidOperationException(
                "No thinking route model found, configure it in `RouteModel.Thinking`"
            );

        switch (thinkingRouteModel.ProviderType)
        {
            case ProviderType.Azure:
                RegisterAzureAI(
                    builder,
                    baseAddress: thinkingRouteModel.Provider.BaseAddress,
                    apiKey: thinkingRouteModel.Provider.ApiKey,
                    deploymentName: thinkingRouteModel.Model,
                    model: thinkingRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Thinking
                );
                break;

            case ProviderType.OpenAI:

                RegisterOpenAI(
                    builder,
                    baseAddress: thinkingRouteModel.Provider.BaseAddress,
                    apiKey: thinkingRouteModel.Provider.ApiKey,
                    deploymentName: thinkingRouteModel.Model,
                    model: thinkingRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Thinking
                );
                break;

            case ProviderType.GitHubModels:

                RegisterGitHubModels(
                    builder,
                    baseAddress: thinkingRouteModel.Provider.BaseAddress,
                    apiKey: thinkingRouteModel.Provider.ApiKey,
                    model: thinkingRouteModel.Model,
                    enableTelemetry: options.EnableTelemetry,
                    providerModelKey: RouteModelKeys.Thinking
                );
                break;
        }
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
                DisableTracing = enableTelemetry,
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
                otel.EnableSensitiveData = builder.Environment.IsDevelopment()
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
                DisableTracing = enableTelemetry,
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
                otel.EnableSensitiveData = builder.Environment.IsDevelopment()
        );
    }
}
