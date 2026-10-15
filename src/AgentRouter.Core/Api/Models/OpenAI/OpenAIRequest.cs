using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRouter.Core.Api.Models.OpenAI;

// =========================
// Request
// =========================

public class OpenAIRequest
{
    public required string Model { get; set; }

    public required List<Message> Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public uint? MaxTokens { get; set; }

    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    public List<string>? Stop { get; set; }

    public bool? Stream { get; set; }

    public List<Tool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public JsonElement? ToolChoice { get; set; }
}

// =========================
// Message
// =========================

public class Message
{
    public required string Role { get; set; }

    public MessageContent? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    public string? Name { get; set; }
}

[JsonConverter(typeof(OpenAIMessageContentConverter))]
public class MessageContent
{
    public string? Text { get; set; }
    public List<ContentPart>? Parts { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ImageUrlPart), "image_url")]
public abstract class ContentPart;

public class TextPart : ContentPart
{
    public required string Text { get; set; }
}

public class ImageUrlPart : ContentPart
{
    [JsonPropertyName("image_url")]
    public required ImageUrl ImageUrl { get; set; }
}

public class ImageUrl
{
    public required string Url { get; set; }
}

// =========================
// Tools
// =========================

public class ToolCall
{
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public required string CallType { get; set; }

    public required FunctionCall Function { get; set; }
}

public class FunctionCall
{
    public required string Name { get; set; }
    public required string Arguments { get; set; }
}

public class Tool
{
    [JsonPropertyName("type")]
    public required string ToolType { get; set; }

    public required Function Function { get; set; }
}

public class Function
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required JsonElement Parameters { get; set; }
}

// =========================
// Response
// =========================

public class OpenAIResponse
{
    public required string Id { get; set; }

    public required string Object { get; set; }

    public ulong Created { get; set; }

    public required string Model { get; set; }

    public required List<Choice> Choices { get; set; }

    public Usage? Usage { get; set; }

    [JsonPropertyName("system_fingerprint")]
    public string? SystemFingerprint { get; set; }
}

public class Choice
{
    public int Index { get; set; }

    public required ChoiceMessage Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class ChoiceMessage
{
    public required string Role { get; set; }

    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }
}

public class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public uint PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public uint CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public uint TotalTokens { get; set; }
}
