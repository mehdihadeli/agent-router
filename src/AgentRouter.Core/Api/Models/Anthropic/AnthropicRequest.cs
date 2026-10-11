using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRouter.Models.Anthropic;

// =========================
// Request
// =========================

public class AnthropicRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<Message> Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public uint MaxTokens { get; set; }

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SystemPrompt? System { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }

    [JsonPropertyName("top_k")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? TopK { get; set; }

    [JsonPropertyName("stop_sequences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StopSequences { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Tool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ToolChoice { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Metadata { get; set; }

    // serde(flatten)
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

// =========================
// System Prompt (untagged)
// =========================

[JsonConverter(typeof(SystemPromptConverter))]
public class SystemPrompt
{
    public string? Single { get; set; }
    public List<SystemMessage>? Multiple { get; set; }
}

public class SystemMessage
{
    [JsonPropertyName("type")]
    public required string MessageType { get; set; }

    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CacheControl { get; set; }
}

// =========================
// Message
// =========================

public class Message
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required MessageContent Content { get; set; }
}

[JsonConverter(typeof(MessageContentConverter))]
public class MessageContent
{
    public string? Text { get; set; }
    public List<ContentBlock>? Blocks { get; set; }
}

// =========================
// Content Blocks (tagged)
// =========================

[JsonConverter(typeof(ContentBlockConverter))]
public abstract class ContentBlock;

public class TextBlock : ContentBlock
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? CacheControl { get; set; }
}

public class ImageBlock : ContentBlock
{
    [JsonPropertyName("source")]
    public required ImageSource Source { get; set; }
}

public class ToolUseBlock : ContentBlock
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("input")]
    public required JsonElement Input { get; set; }
}

public class ToolResultBlock : ContentBlock
{
    [JsonPropertyName("tool_use_id")]
    public required string ToolUseId { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }
}

public class ThinkingBlock : ContentBlock
{
    [JsonPropertyName("thinking")]
    public required string Thinking { get; set; }
}

public class ImageSource
{
    [JsonPropertyName("type")]
    public required string SourceType { get; set; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; set; }

    [JsonPropertyName("data")]
    public required string Data { get; set; }
}

// =========================
// Tool
// =========================

public class Tool
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public required JsonElement InputSchema { get; set; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolType { get; set; }
}

// =========================
// Response
// =========================

public class AnthropicResponse
{
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public required string ResponseType { get; set; }

    public required string Role { get; set; }

    public required List<ResponseContent> Content { get; set; }

    public required string Model { get; set; }

    public string? StopReason { get; set; }

    public string? StopSequence { get; set; }

    public required Usage Usage { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResponseText), "text")]
[JsonDerivedType(typeof(ResponseToolUse), "tool_use")]
[JsonDerivedType(typeof(ResponseThinking), "thinking")]
public abstract class ResponseContent;

public class ResponseText : ResponseContent
{
    public required string Text { get; set; }
}

public class ResponseToolUse : ResponseContent
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required JsonElement Input { get; set; }
}

public class ResponseThinking : ResponseContent
{
    public required string Thinking { get; set; }
}

public class Usage
{
    [JsonPropertyName("input_tokens")]
    public uint InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public uint OutputTokens { get; set; }
}
