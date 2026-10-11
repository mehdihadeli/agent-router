using AgentRouter.Converters;
using AgentRouter.Models.Anthropic;
using Microsoft.Extensions.AI;

namespace AgentRouter.Features.CreatingMessage;

/// <summary>
/// Handles non-streaming (synchronous) requests.
/// Calls <see cref="IChatClient.GetResponseAsync"/> and converts the MEAI response
/// back to Anthropic format via <see cref="AnthropicToMEAIConverter"/>.
/// </summary>
public static class AnthropicNonStreamingRequestHandler
{
    public static async Task<IResult> HandleAsync(
        IChatClient chatClient,
        AnthropicRequest anthropicRequest,
        string modelId,
        CancellationToken ct
    )
    {
        var (messages, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(
            anthropicRequest
        );

        // Send the provider-facing model name, not the gateway route.
        options.ModelId = modelId;

        var chatResponse = await chatClient.GetResponseAsync(messages, options, ct);

        var anthropicResponse = AnthropicToMEAIConverter.ToAnthropicResponse(
            chatResponse,
            anthropicRequest.Model
        );

        return Results.Ok(anthropicResponse);
    }
}
