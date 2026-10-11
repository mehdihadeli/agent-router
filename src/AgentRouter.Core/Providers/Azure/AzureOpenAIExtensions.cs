using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgentRouter.Shared.AIProviders.Azure;

public static class AzureOpenAIExtensions
{
    private static readonly string DefaultExtensionsAISourceName =
        "Experimental.Microsoft.Extensions.AI";

    public static AzureOpenAIApiClientBuilder AddAzureOpenAIClient(
        this IHostApplicationBuilder builder,
        AzureOpenAIApiClientSettings settings,
        object? serviceKey = null
    )
    {
        // Use the main endpoint if provided, otherwise fall back to chat endpoint
        ArgumentException.ThrowIfNullOrEmpty(settings.Endpoint, nameof(settings.Endpoint));
        var endpoint = new Uri(settings.Endpoint);

        ArgumentException.ThrowIfNullOrEmpty(settings.ApiKey, nameof(settings.ApiKey));
        var credential = new ApiKeyCredential(settings.ApiKey);

        var azureOpenAIClient = new AzureOpenAIClient(endpoint: endpoint, credential: credential);
        if (serviceKey is not null)
        {
            builder.Services.AddKeyedSingleton(serviceKey, azureOpenAIClient);
        }
        else
        {
            builder.Services.TryAddSingleton(azureOpenAIClient);
        }

        return new AzureOpenAIApiClientBuilder(builder, serviceKey, settings.DisableTracing);
    }

    public static AzureOpenAIApiClientBuilder AddAzureOpenAIChatClient(
        this AzureOpenAIApiClientBuilder azureOpenAiApiClientBuilder,
        string deploymentName,
        string? model = null,
        Action<ChatClientBuilder>? chatClientBuilderConfigs = null,
        Action<OpenTelemetryChatClient>? configureOpenTelemetry = null,
        string? openTelemetrySourceName = null,
        bool useCache = false
    )
    {
        var chatClientBuilder = azureOpenAiApiClientBuilder.HostBuilder.Services.AddKeyedChatClient(azureOpenAiApiClientBuilder.ServiceKey!, sp =>
        {
            var azureOpenAiClient = sp.GetRequiredKeyedService<AzureOpenAIClient>(
                azureOpenAiApiClientBuilder.ServiceKey
            );
            // `OpenAIChatClient` implements `IChatClient`
            // https://github.com/dotnet/extensions/blob/1eb963e2a9ec2a6501af082f058c3420ea5004a8/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs#L110
            IChatClient chatClient = azureOpenAiClient
                .GetChatClient(deploymentName)
                .AsIChatClient();

            return chatClient;
        });

        if (!azureOpenAiApiClientBuilder.DisableTracing)
        {
            chatClientBuilder.UseOpenTelemetry(
                sourceName: openTelemetrySourceName ?? DefaultExtensionsAISourceName,
                configure: configureOpenTelemetry
            );
        }

        if (useCache)
        {
            chatClientBuilder.UseDistributedCache();
        }

        // NOTE: Do NOT call UseFunctionInvocation() here. This gateway is a pure proxy —
        // tool calls must be returned verbatim as FunctionCallContent to the caller (Claude Code).
        // Auto-invocation would silently execute the stub delegate and swallow the tool call.
        chatClientBuilder = chatClientBuilder.UseLogging();

        chatClientBuilderConfigs?.Invoke(chatClientBuilder);

        return azureOpenAiApiClientBuilder;
    }

    public static ChatClientBuilder AddAzureOpenAIChatClient(
        IServiceCollection services,
        AzureOpenAIClient azureOpenAiClient,
        string deploymentName,
        string? model = null,
        bool disableTracing = false,
        Action<OpenTelemetryChatClient>? configureOpenTelemetry = null,
        string? openTelemetrySourceName = null,
        bool useCache = false
    )
    {
        var chatClientBuilder = services.AddChatClient(sp =>
        {
            // `OpenAIChatClient` implements `IChatClient`
            // https://github.com/dotnet/extensions/blob/1eb963e2a9ec2a6501af082f058c3420ea5004a8/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs#L110
            IChatClient chatClient = azureOpenAiClient
                .GetChatClient(deploymentName)
                .AsIChatClient();

            return chatClient;
        });

        if (!disableTracing)
        {
            chatClientBuilder.UseOpenTelemetry(
                sourceName: openTelemetrySourceName ?? DefaultExtensionsAISourceName,
                configure: configureOpenTelemetry
            );
        }

        if (useCache)
        {
            chatClientBuilder.UseDistributedCache();
        }

        return chatClientBuilder.UseLogging();
    }

    public static ChatClientBuilder AddAzureOpenAIChatClient(
        IServiceCollection services,
        string azureOpenAIClientName,
        string deploymentName,
        string? model = null,
        bool disableTracing = false,
        Action<OpenTelemetryChatClient>? configureOpenTelemetry = null,
        string? openTelemetrySourceName = null,
        bool useCache = false
    )
    {
        var chatClientBuilder = services.AddChatClient(sp =>
        {
            var azureOpenAIClient = sp.GetRequiredKeyedService<AzureOpenAIClient>(
                azureOpenAIClientName
            );
            // the implementor of `IOllamaApiClient` also implements `IChatClient` like `OllamaApiClient`
            // `OpenAIChatClient` implements `IChatClient`
            // https://github.com/dotnet/extensions/blob/1eb963e2a9ec2a6501af082f058c3420ea5004a8/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs#L110
            IChatClient chatClient = azureOpenAIClient
                .GetChatClient(deploymentName)
                .AsIChatClient();

            return chatClient;
        });

        if (!disableTracing)
        {
            chatClientBuilder.UseOpenTelemetry(
                sourceName: openTelemetrySourceName ?? DefaultExtensionsAISourceName,
                configure: configureOpenTelemetry
            );
        }

        if (useCache)
        {
            chatClientBuilder.UseDistributedCache();
        }

        return chatClientBuilder.UseLogging();
    }
}

public class AzureOpenAIApiClientBuilder(
    IHostApplicationBuilder hostBuilder,
    object? serviceKey,
    bool disableTracing
)
{
    /// <summary>
    /// The host application builder used to configure the application.
    /// </summary>
    public IHostApplicationBuilder HostBuilder { get; } = hostBuilder;

    /// <summary>
    /// Gets the service key used to register the <see cref="AzureOpenAIClient"/> service, if any.
    /// </summary>
    public object? ServiceKey { get; } = serviceKey;

    /// <summary>
    /// Gets a flag indicating whether tracing should be disabled.
    /// </summary>
    public bool DisableTracing { get; } = disableTracing;
}

public class AzureOpenAIApiClientSettings
{
    public required string Endpoint { get; set; } = default!;
    public required string ApiKey { get; set; } = default!;
    public bool DisableTracing { get; set; }
}
