using AgentRouter.Models.Anthropic;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Constants;
using AgentRouter.Shared.Routes;
using AgentRouter.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentRouter.Features.CreatingMessage;

public static class CreateMessageEndpoint
{
    public static IEndpointRouteBuilder MapCreatingMessage(this IEndpointRouteBuilder builder)
    {
        builder.MapPost(
            "/v1/messages",
            async (
                AnthropicRequest anthropicRequest,
                HttpContext httpContext,
                IServiceProvider sp,
                IOptions<ClaudeGatewayOptions> gatewayOptions,
                CancellationToken ct
            ) =>
            {
                AnthropicRequestValidator.ThrowIfInvalid(anthropicRequest);

                var selectedModel = RouteModelFactory.ResolveRequestModel(
                    gatewayOptions.Value,
                    anthropicRequest.Model
                );
                var chatClient = ResolveChatClient(
                    sp,
                    gatewayOptions.Value,
                    $"{selectedModel.Name}/{selectedModel.Model}"
                );

                // The provider must receive the bare model name (e.g. "deepseek-flash"),
                // not the gateway route ("openai/deepseek-flash").
                var modelId = selectedModel.Model;

                if (anthropicRequest.Stream is true)
                {
                    return await AnthropicStreamingRequestHandler.HandleAsync(
                        chatClient,
                        anthropicRequest,
                        modelId,
                        httpContext,
                        ct
                    );
                }

                return await AnthropicNonStreamingRequestHandler.HandleAsync(
                    chatClient,
                    anthropicRequest,
                    modelId,
                    ct
                );
            }
        );

        return builder;
    }

    /// <summary>
    /// Resolves the keyed <see cref="IChatClient"/> for the given model string.
    /// Falls back to the "default" key, then to an unkeyed registration.
    /// </summary>
    private static IChatClient ResolveChatClient(
        IServiceProvider sp,
        ClaudeGatewayOptions gatewayOptions,
        string model
    )
    {
        var serviceKey = RouteModelFactory.ResolveServiceKey(gatewayOptions, model);

        return sp.GetKeyedService<IChatClient>(serviceKey)
            ?? sp.GetKeyedService<IChatClient>(RouteModelKeys.Default)
            ?? sp.GetService<IChatClient>()
            ?? throw new InvalidOperationException(
                $"No IChatClient registered for model '{model}' (key '{serviceKey}'). "
                    + "Check your provider/model configuration."
            );
    }
}
