using System.Text.Json;
using ClaudeCodeGateway.Models.Anthropic;
using Microsoft.Extensions.AI;

namespace ClaudeCodeGateway.Converters;

/// <summary>
/// Converts between Anthropic Messages API models and MEAI types
/// (<see cref="ChatMessage"/>, <see cref="ChatOptions"/>, <see cref="ChatResponse"/>).
///
/// Request:  <see cref="AnthropicRequest"/> → <see cref="ChatMessage"/> list + <see cref="ChatOptions"/>
/// Response: <see cref="ChatResponse"/>     → <see cref="AnthropicResponse"/>
/// </summary>
public static class AnthropicToMEAIConverter
{
    // ═════════════════════════════════════════════════════════════════════
    // Request:  AnthropicRequest → MEAI ChatMessage[] + ChatOptions
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts an <see cref="AnthropicRequest"/> directly into MEAI
    /// <see cref="ChatMessage"/> list and <see cref="ChatOptions"/>.
    /// </summary>
    public static (List<ChatMessage> Messages, ChatOptions Options) ToMEAIChatCompletionRequest(
        AnthropicRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var messages = BuildMEAIMessages(request);
        var options = BuildMEAIOptions(request);

        return (messages, options);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Response: MEAI ChatResponse → AnthropicResponse
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts an MEAI <see cref="ChatResponse"/> directly into an <see cref="AnthropicResponse"/>.
    /// </summary>
    public static AnthropicResponse ToAnthropicResponse(ChatResponse? chatResponse, string model)
    {
        var allContent = chatResponse?.Messages
            .SelectMany(m => m.Contents)
            .ToList() ?? [];

        var content = new List<ResponseContent>();

        // Text
        var responseText = string.Join("", allContent.OfType<TextContent>().Select(t => t.Text));
        if (!string.IsNullOrEmpty(responseText))
            content.Add(new ResponseText { Text = responseText });

        // Tool calls
        foreach (var fc in allContent.OfType<FunctionCallContent>())
        {
            content.Add(new ResponseToolUse
            {
                Id    = fc.CallId ?? "toolu_" + Guid.NewGuid().ToString("N"),
                Name  = fc.Name,
                Input = fc.Arguments is not null
                    ? JsonSerializer.SerializeToElement(fc.Arguments)
                    : JsonSerializer.SerializeToElement(new { }),
            });
        }

        var hasToolUse = content.OfType<ResponseToolUse>().Any();

        return new AnthropicResponse
        {
            Id           = "msg_" + Guid.NewGuid().ToString("N"),
            ResponseType = "message",
            Role         = "assistant",
            Model        = model,
            StopReason   = hasToolUse ? "tool_use" : "end_turn",
            StopSequence = null,
            Content      = content,
            Usage = new Usage
            {
                InputTokens  = 0,
                OutputTokens = 0,
            },
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    // MEAI messages builder (directly from AnthropicRequest)
    // ═════════════════════════════════════════════════════════════════════

    private static List<ChatMessage> BuildMEAIMessages(AnthropicRequest request)
    {
        var messages = new List<ChatMessage>();

        // System prompt → system message
        var systemText = GetSystemText(request.System);
        if (!string.IsNullOrWhiteSpace(systemText))
            messages.Add(new ChatMessage(ChatRole.System, systemText));

        // Convert each Anthropic message
        foreach (var m in request.Messages)
            AppendAnthropicMessage(messages, m);

        return messages;
    }

    private static void AppendAnthropicMessage(
        List<ChatMessage> output,
        Models.Anthropic.Message message)
    {
        var role = ParseRole(message.Role);

        // Plain text content
        if (message.Content.Text is not null)
        {
            output.Add(new ChatMessage(role, message.Content.Text));
            return;
        }

        var blocks = message.Content.Blocks ?? [];
        if (blocks.Count == 0)
        {
            output.Add(new ChatMessage(role, string.Empty));
            return;
        }

        // Content blocks — may contain text, tool_use, tool_result interleaved.
        // Text + tool_use belong on the same assistant message.
        // tool_result must be a separate Tool-role message.
        var currentRole = role;
        var contentBuffer = new List<AIContent>();

        void FlushBufferIfAny()
        {
            if (contentBuffer.Count == 0) return;
            output.Add(new ChatMessage(currentRole, [.. contentBuffer]));
            contentBuffer.Clear();
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case TextBlock tb:
                    contentBuffer.Add(new TextContent(tb.Text));
                    break;

                case ThinkingBlock:
                    // Skip thinking blocks — not forwarded to MEAI providers.
                    break;

                case ImageBlock:
                    throw new NotSupportedException(
                        "Anthropic image blocks are not yet supported.");

                case ToolUseBlock tu:
                {
                    // tool_use is always from the assistant.
                    if (currentRole != ChatRole.Assistant)
                    {
                        FlushBufferIfAny();
                        currentRole = ChatRole.Assistant;
                    }

                    IDictionary<string, object?>? args = null;
                    if (tu.Input.ValueKind != JsonValueKind.Undefined)
                    {
                        try
                        {
                            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                                tu.Input.GetRawText());
                        }
                        catch { /* leave null */ }
                    }

                    contentBuffer.Add(new FunctionCallContent(tu.Id, tu.Name, args));
                    break;
                }

                case ToolResultBlock tr:
                {
                    // tool_result must be a separate Tool-role message.
                    FlushBufferIfAny();
                    output.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(tr.ToolUseId, tr.Content ?? string.Empty)]));
                    currentRole = role; // restore original role
                    break;
                }
            }
        }

        FlushBufferIfAny();
    }

    // ═════════════════════════════════════════════════════════════════════
    // MEAI options builder (directly from AnthropicRequest)
    // ═════════════════════════════════════════════════════════════════════

    private static ChatOptions BuildMEAIOptions(AnthropicRequest request)
    {
        var options = new ChatOptions
        {
            ModelId         = request.Model,
            Temperature     = request.Temperature,
            MaxOutputTokens = request.MaxTokens == 0 ? null : (int?)request.MaxTokens,
        };

        // Resolve tools (strongly-typed or from Extra)
        var tools = ResolveTools(request);

        if (tools is { Count: > 0 })
        {
            options.Tools = tools
                .Select(t =>
                {
                    Func<AIFunctionArguments, object?> stub = static (_) => null;
                    return (AITool)AIFunctionFactory.Create(stub, t.Name, t.Description);
                })
                .ToList();

            // Map tool choice — default to Auto when tools are present.
            var toolChoice = ResolveToolChoice(request);
            options.ToolMode = IsToolChoiceNone(toolChoice)
                ? ChatToolMode.None
                : ChatToolMode.Auto;
        }

        return options;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Internal: kept for AnthropicMEAIConverter.ToMEAIToolChoice and tests
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Converts Anthropic tool_choice → OpenAI-shaped tool_choice JsonElement.</summary>
    internal static JsonElement? ConvertToolChoice(JsonElement? anthropicToolChoice)
    {
        if (anthropicToolChoice is null) return null;
        var tc = anthropicToolChoice.Value;

        if (tc.ValueKind == JsonValueKind.String)
        {
            var value = tc.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (string.Equals(value, "any", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement("required");
            return JsonSerializer.SerializeToElement(value.ToLowerInvariant());
        }

        if (tc.ValueKind == JsonValueKind.Object)
        {
            if (!tc.TryGetProperty("type", out var typeEl)) return null;
            var type = typeEl.GetString();
            if (string.IsNullOrWhiteSpace(type)) return null;

            if (string.Equals(type, "tool", StringComparison.OrdinalIgnoreCase))
            {
                if (!tc.TryGetProperty("name", out var nameEl)) return null;
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name)) return null;
                return JsonSerializer.SerializeToElement(new { type = "function", function = new { name } });
            }

            return tc;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═════════════════════════════════════════════════════════════════════

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "assistant" => ChatRole.Assistant,
        "system"    => ChatRole.System,
        "tool"      => ChatRole.Tool,
        _           => ChatRole.User,
    };

    private static string GetSystemText(SystemPrompt? system)
    {
        if (system is null) return string.Empty;
        if (system.Single is not null) return system.Single;
        if (system.Multiple is not null) return string.Join("\n", system.Multiple.Select(m => m.Text));
        return string.Empty;
    }

    private static List<Models.Anthropic.Tool>? ResolveTools(AnthropicRequest request)
    {
        var tools = request.Tools;
        if (tools is null && request.Extra is not null
            && request.Extra.TryGetValue("tools", out var toolsEl))
        {
            try { tools = JsonSerializer.Deserialize<List<Models.Anthropic.Tool>>(toolsEl); }
            catch { /* ignore */ }
        }
        return tools;
    }

    private static JsonElement? ResolveToolChoice(AnthropicRequest request)
    {
        JsonElement? toolChoice = request.ToolChoice;
        if (toolChoice is null && request.Extra is not null
            && request.Extra.TryGetValue("tool_choice", out var tcEl))
        {
            toolChoice = tcEl;
        }
        return toolChoice;
    }

    private static bool IsToolChoiceNone(JsonElement? toolChoice)
    {
        return toolChoice is { ValueKind: JsonValueKind.String }
               && string.Equals(toolChoice.Value.GetString(), "none",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement EnsureJsonSchemaObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false });

        if (element.TryGetProperty("type", out _))
            return element;

        using var doc = JsonDocument.Parse(element.GetRawText());
        var amended = new Dictionary<string, object?> { ["type"] = "object" };
        foreach (var prop in doc.RootElement.EnumerateObject())
            amended[prop.Name] = prop.Value;
        return JsonSerializer.SerializeToElement(amended);
    }
}
