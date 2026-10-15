using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;

namespace AgentRouter.Core.Providers.OpenAI;

public static class OpenAIExtensiosn
{
    private static readonly string DefaultExtensionsAISourceName =
        "Experimental.Microsoft.Extensions.AI";

    public static OpenAIApiClientBuilder AddOpenAIClient(
        this IHostApplicationBuilder builder,
        OpenAIApiClientSettings settings,
        object? serviceKey = null
    ) => AddOpenAIClient(builder, settings, serviceKey, 1);

    public static OpenAIApiClientBuilder AddOpenAIClient(
        this IHostApplicationBuilder builder,
        OpenAIApiClientSettings settings,
        object? serviceKey,
        int maxProviderAttempts
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxProviderAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxProviderAttempts, 8);
        ArgumentException.ThrowIfNullOrEmpty(settings.ApiKey, nameof(settings.ApiKey));

        // supports both `openai` and `openrouter` endpoints
        string openaiEndpoint = settings.Endpoint ?? "https://api.openai.com/v1";
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(openaiEndpoint),
            RetryPolicy = new ClientRetryPolicy(maxProviderAttempts - 1),
        };
        settings.ConfigureClient?.Invoke(clientOptions);
        var openAiClient = new OpenAIClient(new ApiKeyCredential(settings.ApiKey), clientOptions);

        if (serviceKey is not null)
        {
            builder.Services.AddKeyedSingleton(serviceKey, openAiClient);
        }
        else
        {
            builder.Services.TryAddSingleton(openAiClient);
        }

        return new OpenAIApiClientBuilder(builder, serviceKey, settings.DisableTracing);
    }

    public static OpenAIApiClientBuilder AddOpenAIChatClient(
        this OpenAIApiClientBuilder builder,
        string deploymentName,
        string? model = null,
        Action<ChatClientBuilder>? chatClientBuilderConfigs = null,
        Action<OpenTelemetryChatClient>? configureOpenTelemetry = null,
        string? openTelemetrySourceName = null,
        bool useCache = false
    )
    {
        var chatClientBuilder = builder.HostBuilder.Services.AddKeyedChatClient(
            builder.ServiceKey!,
            sp =>
            {
                var openAiClient = sp.GetRequiredKeyedService<OpenAIClient>(builder.ServiceKey);
                // `OpenAIChatClient` implements `IChatClient`
                // https://github.com/dotnet/extensions/blob/1eb963e2a9ec2a6501af082f058c3420ea5004a8/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs#L110
                IChatClient chatClient = openAiClient.GetChatClient(deploymentName).AsIChatClient();

                return chatClient;
            }
        );

        if (!builder.DisableTracing)
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
        chatClientBuilder = chatClientBuilder.UseLogging();

        chatClientBuilderConfigs?.Invoke(chatClientBuilder);

        return builder;
    }

    public static ChatClientBuilder AddOpenAIChatClient(
        IServiceCollection services,
        OpenAIClient openAiClient,
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
            IChatClient chatClient = openAiClient.GetChatClient(deploymentName).AsIChatClient();

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

    public static ChatClientBuilder AddOpenAIChatClient(
        IServiceCollection services,
        string openAIClientName,
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
            var openAIClient = sp.GetRequiredKeyedService<OpenAIClient>(openAIClientName);
            // `OpenAIChatClient` implements `IChatClient`
            // https://github.com/dotnet/extensions/blob/1eb963e2a9ec2a6501af082f058c3420ea5004a8/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs#L110
            IChatClient chatClient = openAIClient.GetChatClient(deploymentName).AsIChatClient();

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
