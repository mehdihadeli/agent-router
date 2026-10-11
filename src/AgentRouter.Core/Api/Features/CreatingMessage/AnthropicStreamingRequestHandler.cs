using AgentRouter.Converters;
using AgentRouter.Models.Anthropic;
using Microsoft.Extensions.AI;

namespace AgentRouter.Features.CreatingMessage;

/// <summary>
/// Handles streaming (SSE) requests.
/// Calls <see cref="IChatClient.GetStreamingResponseAsync"/> to get the provider stream,
/// then delegates to <see cref="MEAIStreamToAnthropicStreamConverter"/> to convert
/// the MEAI stream into Anthropic-compatible SSE events.
///
/// https://platform.claude.com/docs/en/build-with-claude/streaming
/// </summary>
public static class AnthropicStreamingRequestHandler
{
    public static async Task<IResult> HandleAsync(
        IChatClient chatClient,
        AnthropicRequest anthropicRequest,
        string modelId,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        // Convert Anthropic request → MEAI messages + options.
        var (messages, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(
            anthropicRequest
        );

        // Send the provider-facing model name, not the gateway route.
        options.ModelId = modelId;

        // Get the streaming response from the provider.
        var stream = chatClient.GetStreamingResponseAsync(messages, options, ct);

        // Convert the MEAI stream → Anthropic SSE events.
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            httpContext,
            stream,
            anthropicRequest.Model,
            ct
        );

        return Results.Empty;
    }
}
