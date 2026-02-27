using ClaudeCodeGateway.Models.Anthropic;
using ClaudeCodeGateway.Validation;
using Common;
using Common.Constants;
using Common.Routes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ClaudeCodeGateway.Features.CreatingMessage;

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

                var chatClient = ResolveChatClient(sp, gatewayOptions.Value, anthropicRequest.Model);

                if (anthropicRequest.Stream is true)
                {
                    return await AnthropicStreamingRequestHandler.HandleAsync(
                        chatClient, anthropicRequest, httpContext, ct);
                }

                return await AnthropicNonStreamingRequestHandler.HandleAsync(
                    chatClient, anthropicRequest, ct);
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
        string model)
    {
        var serviceKey = RouteModelFactory.ResolveServiceKey(gatewayOptions, model);

        return sp.GetKeyedService<IChatClient>(serviceKey)
               ?? sp.GetKeyedService<IChatClient>(RouteModelKeys.Default)
               ?? sp.GetService<IChatClient>()
               ?? throw new InvalidOperationException(
                   $"No IChatClient registered for model '{model}' (key '{serviceKey}'). "
                   + "Check your provider/model configuration.");
    }
}
