using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using Microsoft.Extensions.AI;

namespace AgentRouter.Core.Api.Converters;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResponsesRequest
{
    public string Model { get; set; } = "";
    public JsonElement Input { get; set; }
    public string? Instructions { get; set; }
    public bool Stream { get; set; }
    public bool Store { get; set; }
    public bool Background { get; set; }

    [JsonPropertyName("previous_response_id")]
    public string? PreviousResponseId { get; set; }

    [JsonPropertyName("max_output_tokens")]
    public int? MaxOutputTokens { get; set; }
    public float? Temperature { get; set; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; }
    public JsonElement Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public JsonElement ToolChoice { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public static class ResponsesConverter
{
    private sealed class Function(string name, string? description, JsonElement schema)
        : AIFunctionDeclaration
    {
        public override string Name => name;
        public override string Description => description ?? "";
        public override JsonElement JsonSchema => schema;
    }

    public static (List<ChatMessage> Messages, ChatOptions Options) Convert(
        ResponsesRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new BadRequestException("model is required.");
        if (request.Store || request.Background || request.PreviousResponseId is not null)
            throw new BadRequestException(
                "Only stateless Responses requests are supported: use store=false, full input history, and no background or previous_response_id."
            );
        if (request.MaxOutputTokens is <= 0 || request.Temperature is < 0 or > 2)
            throw new BadRequestException("Invalid max_output_tokens or temperature.");
        var messages = new List<ChatMessage>();
        if (request.Instructions is not null)
            messages.Add(new ChatMessage(ChatRole.System, request.Instructions));
        if (request.Input.ValueKind == JsonValueKind.String)
            messages.Add(new ChatMessage(ChatRole.User, request.Input.GetString()));
        else if (request.Input.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in request.Input.EnumerateArray())
            {
                var type = OptionalString(item, "type") ?? "message";
                if (type == "reasoning")
                {
                    RequireProperties(
                        item,
                        "type",
                        "id",
                        "status",
                        "summary",
                        "content",
                        "encrypted_content"
                    );
                    if (
                        item.TryGetProperty("summary", out var summary)
                        && (
                            summary.ValueKind != JsonValueKind.Array
                            || summary.GetArrayLength() != 0
                        )
                    )
                        throw new BadRequestException(
                            "Reasoning summaries cannot replace original reasoning content."
                        );
                    var text = "";
                    if (item.TryGetProperty("content", out var reasoningParts))
                    {
                        if (reasoningParts.ValueKind != JsonValueKind.Array)
                            throw new BadRequestException("Reasoning content must be an array.");
                        foreach (var part in reasoningParts.EnumerateArray())
                        {
                            RequireProperties(part, "type", "text");
                            if (RequiredString(part, "type") != "reasoning_text")
                                throw new BadRequestException("Unsupported reasoning content.");
                            text += RequiredString(part, "text");
                        }
                    }
                    messages.Add(
                        new ChatMessage(
                            ChatRole.Assistant,
                            [
                                new TextReasoningContent(text)
                                {
                                    ProtectedData = OptionalString(item, "encrypted_content"),
                                },
                            ]
                        )
                    );
                }
                else if (type == "function_call")
                {
                    RequireProperties(item, "type", "id", "status", "call_id", "name", "arguments");
                    var arguments =
                        JsonSerializer.Deserialize<Dictionary<string, object?>>(
                            RequiredString(item, "arguments")
                        )
                        ?? throw new BadRequestException(
                            "Function arguments must be a JSON object."
                        );
                    messages.Add(
                        new ChatMessage(
                            ChatRole.Assistant,
                            [
                                new FunctionCallContent(
                                    RequiredString(item, "call_id"),
                                    RequiredString(item, "name"),
                                    arguments
                                ),
                            ]
                        )
                    );
                }
                else if (type == "function_call_output")
                {
                    RequireProperties(item, "type", "id", "status", "call_id", "output");
                    messages.Add(
                        new ChatMessage(
                            ChatRole.Tool,
                            [
                                new FunctionResultContent(
                                    RequiredString(item, "call_id"),
                                    RequiredString(item, "output")
                                ),
                            ]
                        )
                    );
                }
                else if (type == "message")
                {
                    RequireProperties(item, "type", "id", "status", "role", "content");
                    var role = RequiredString(item, "role") switch
                    {
                        "user" => ChatRole.User,
                        "assistant" => ChatRole.Assistant,
                        "system" => ChatRole.System,
                        "developer" => new ChatRole("developer"),
                        _ => throw new BadRequestException("Unsupported message role."),
                    };
                    if (!item.TryGetProperty("content", out var content))
                        throw new BadRequestException("Message content is required.");
                    if (content.ValueKind == JsonValueKind.String)
                        messages.Add(new ChatMessage(role, content.GetString()));
                    else if (content.ValueKind == JsonValueKind.Array)
                    {
                        var parts = new List<AIContent>();
                        foreach (var part in content.EnumerateArray())
                        {
                            RequireProperties(part, "type", "text", "annotations");
                            if (
                                RequiredString(part, "type") is not ("input_text" or "output_text")
                                || (
                                    part.TryGetProperty("annotations", out var annotations)
                                    && annotations.GetArrayLength() != 0
                                )
                            )
                                throw new BadRequestException(
                                    "Only unannotated text content is supported."
                                );
                            parts.Add(new TextContent(RequiredString(part, "text")));
                        }
                        messages.Add(new ChatMessage(role, parts));
                    }
                    else
                        throw new BadRequestException("Invalid message content.");
                }
                else
                    throw new BadRequestException(
                        "Unsupported Responses input item. Custom tools and continuation items cannot be translated."
                    );
            }
        }
        else
            throw new BadRequestException("input must be text or an array of input items.");
        for (var index = messages.Count - 1; index > 0; index--)
        {
            if (
                messages[index].Role == ChatRole.Assistant
                && messages[index - 1].Role == ChatRole.Assistant
            )
            {
                foreach (var content in messages[index].Contents)
                    messages[index - 1].Contents.Add(content);
                messages.RemoveAt(index);
            }
        }
        var options = new ChatOptions
        {
            ModelId = request.Model,
            MaxOutputTokens = request.MaxOutputTokens,
            Temperature = request.Temperature,
            AllowMultipleToolCalls = request.ParallelToolCalls,
        };
        if (request.Tools.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            if (request.Tools.ValueKind != JsonValueKind.Array)
                throw new BadRequestException("tools must be an array.");
            options.Tools = [];
            foreach (var tool in request.Tools.EnumerateArray())
            {
                RequireProperties(tool, "type", "name", "description", "parameters", "strict");
                if (RequiredString(tool, "type") != "function")
                    throw new BadRequestException(
                        "Only client-executed function tools are supported."
                    );
                if (
                    tool.TryGetProperty("strict", out var strict)
                    && strict.ValueKind == JsonValueKind.True
                )
                    throw new BadRequestException(
                        "Strict schema enforcement is not supported by the cross-provider bridge."
                    );
                if (
                    !tool.TryGetProperty("parameters", out var schema)
                    || schema.ValueKind != JsonValueKind.Object
                )
                    throw new BadRequestException(
                        "Function parameters must be a JSON schema object."
                    );
                options.Tools.Add(
                    new Function(
                        RequiredString(tool, "name"),
                        OptionalString(tool, "description"),
                        schema.Clone()
                    )
                );
            }
        }
        options.ToolMode = request.ToolChoice.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => ChatToolMode.Auto,
            JsonValueKind.String => request.ToolChoice.GetString() switch
            {
                "auto" => ChatToolMode.Auto,
                "none" => ChatToolMode.None,
                "required" => ChatToolMode.RequireAny,
                _ => throw new BadRequestException("Unsupported tool_choice."),
            },
            JsonValueKind.Object => NamedTool(request.ToolChoice),
            _ => throw new BadRequestException("Invalid tool_choice."),
        };
        if (
            options.ToolMode is RequiredChatToolMode required
            && (
                options.Tools is not { Count: > 0 }
                || (
                    required.RequiredFunctionName is not null
                    && !options.Tools.Any(tool => tool.Name == required.RequiredFunctionName)
                )
            )
        )
            throw new BadRequestException("Required tool must be declared.");
        return (messages, options);
    }

    private static ChatToolMode NamedTool(JsonElement choice)
    {
        RequireProperties(choice, "type", "name");
        if (RequiredString(choice, "type") != "function")
            throw new BadRequestException("Unsupported named tool_choice.");
        return ChatToolMode.RequireSpecific(RequiredString(choice, "name"));
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property)
        ?? throw new BadRequestException($"{property} must be a string.");

    private static string? OptionalString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new BadRequestException("Expected an object.");
        if (
            !element.TryGetProperty(property, out var value)
            || value.ValueKind == JsonValueKind.Null
        )
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new BadRequestException($"{property} must be a string.");
        return value.GetString();
    }

    private static void RequireProperties(JsonElement element, params string[] allowed)
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Any(property => !allowed.Contains(property.Name))
        )
            throw new BadRequestException(
                "Unsupported fields in Responses input or tool declaration."
            );
    }

    internal static object Envelope(
        string id,
        long createdAt,
        string model,
        string status,
        IReadOnlyList<object> output,
        UsageDetails? usage = null,
        string? incomplete = null
    ) =>
        new
        {
            id,
            @object = "response",
            created_at = createdAt,
            model,
            status,
            output,
            store = false,
            error = (object?)null,
            incomplete_details = incomplete is null ? null : new { reason = incomplete },
            usage = usage is null
                ? null
                : new
                {
                    input_tokens = usage.InputTokenCount,
                    output_tokens = usage.OutputTokenCount,
                    total_tokens = usage.TotalTokenCount,
                },
        };

    internal static object TextItem(string id, string text, string status) =>
        new
        {
            id,
            type = "message",
            role = "assistant",
            status,
            content = new[] { TextPart(text) },
        };

    internal static object TextPart(string text) =>
        new
        {
            type = "output_text",
            text,
            annotations = Array.Empty<object>(),
        };

    internal static object FunctionItem(string id, FunctionCallContent call, string status) =>
        new
        {
            id,
            type = "function_call",
            status,
            call_id = call.CallId,
            name = call.Name,
            arguments = JsonSerializer.Serialize(
                call.Arguments ?? new Dictionary<string, object?>()
            ),
        };

    internal static object ReasoningItem(
        string id,
        string text,
        string status,
        string? protectedData = null
    ) =>
        new
        {
            id,
            type = "reasoning",
            status,
            summary = Array.Empty<object>(),
            content = new[] { ReasoningPart(text) },
            encrypted_content = protectedData,
        };

    internal static object ReasoningPart(string text) => new { type = "reasoning_text", text };

    public static object ConvertResponse(ChatResponse response, string model)
    {
        var output = new List<object>();
        foreach (var content in response.Messages.SelectMany(message => message.Contents))
            output.Add(
                content switch
                {
                    TextReasoningContent reasoning => ReasoningItem(
                        "rs_" + Guid.NewGuid().ToString("N"),
                        reasoning.Text,
                        "completed",
                        reasoning.ProtectedData
                    ),
                    TextContent text => TextItem(
                        "msg_" + Guid.NewGuid().ToString("N"),
                        text.Text,
                        "completed"
                    ),
                    FunctionCallContent call => FunctionItem(
                        "fc_" + Guid.NewGuid().ToString("N"),
                        call,
                        "completed"
                    ),
                    _ => throw new InvalidOperationException(
                        "Unsupported upstream Responses content."
                    ),
                }
            );
        var incomplete =
            response.FinishReason == ChatFinishReason.Length ? "max_output_tokens"
            : response.FinishReason == ChatFinishReason.ContentFilter ? "content_filter"
            : null;
        return Envelope(
            "resp_" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model,
            incomplete is null ? "completed" : "incomplete",
            output,
            response.Usage,
            incomplete
        );
    }
}
