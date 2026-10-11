using System.Text.Json;
using AgentRouter.Models.Anthropic;
using Microsoft.Extensions.AI;

namespace AgentRouter.Converters;

/// <summary>
/// Convenience facade for <see cref="AnthropicToMEAIConverter"/>.
/// </summary>
public static class AnthropicMEAIConverter
{
    public static (List<ChatMessage> Messages, ChatOptions Options) ToMEAIChatCompletionRequest(
        AnthropicRequest request) =>
        AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(request);

    public static AnthropicResponse ToAnthropicResponse(ChatResponse? chatResponse, string model) =>
        AnthropicToMEAIConverter.ToAnthropicResponse(chatResponse, model);

    public static JsonElement? ToMEAIToolChoice(JsonElement? anthropicToolChoice) =>
        AnthropicToMEAIConverter.ConvertToolChoice(anthropicToolChoice);
}
