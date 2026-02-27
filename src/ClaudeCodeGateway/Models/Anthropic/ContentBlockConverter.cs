using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeGateway.Models.Anthropic;

/// <summary>
/// Claude Code sometimes emits Anthropic-style content blocks without the "type" discriminator.
/// System.Text.Json's polymorphic support requires a discriminator, so we infer the block type
/// from the available properties.
/// </summary>
public sealed class ContentBlockConverter : JsonConverter<ContentBlock>
{
    public override ContentBlock Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Claude Code can send string blocks in arrays.
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString() ?? string.Empty;
            return new TextBlock { Text = text };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("ContentBlock must be an object or string.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var obj = doc.RootElement;

        if (obj.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString();
            return type switch
            {
                "text" => JsonSerializer.Deserialize<TextBlock>(obj, options)!,
                "image" => JsonSerializer.Deserialize<ImageBlock>(obj, options)!,
                "tool_use" => JsonSerializer.Deserialize<ToolUseBlock>(obj, options)!,
                "tool_result" => JsonSerializer.Deserialize<ToolResultBlock>(obj, options)!,
                "thinking" => JsonSerializer.Deserialize<ThinkingBlock>(obj, options)!,
                _ => throw new JsonException($"Unknown content block type '{type}'.")
            };
        }

        // No discriminator. Infer from known property shapes.
        if (obj.TryGetProperty("tool_use_id", out _))
        {
            // tool_result
            if (!obj.TryGetProperty("content", out var contentEl))
                throw new JsonException("tool_result block missing 'content'.");

            return new ToolResultBlock
            {
                ToolUseId = obj.GetProperty("tool_use_id").GetString() ?? string.Empty,
                Content = contentEl.ValueKind == JsonValueKind.String ? contentEl.GetString() ?? string.Empty : contentEl.GetRawText(),
                IsError = obj.TryGetProperty("is_error", out var isErrEl) && isErrEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? isErrEl.GetBoolean()
                    : null,
            };
        }

        if (obj.TryGetProperty("id", out _) && obj.TryGetProperty("name", out _) && obj.TryGetProperty("input", out var inputEl))
        {
            // tool_use
            return new ToolUseBlock
            {
                Id = obj.GetProperty("id").GetString() ?? string.Empty,
                Name = obj.GetProperty("name").GetString() ?? string.Empty,
                Input = inputEl.Clone(),
            };
        }

        if (obj.TryGetProperty("thinking", out var thinkingEl))
        {
            return new ThinkingBlock
            {
                Thinking = thinkingEl.ValueKind == JsonValueKind.String ? thinkingEl.GetString() ?? string.Empty : thinkingEl.GetRawText(),
            };
        }

        if (obj.TryGetProperty("text", out var textEl))
        {
            return new TextBlock
            {
                Text = textEl.ValueKind == JsonValueKind.String ? textEl.GetString() ?? string.Empty : textEl.GetRawText(),
                CacheControl = obj.TryGetProperty("cache_control", out var cc) ? cc.Clone() : null,
            };
        }

        // Unknown block shape.
        throw new JsonException("Unable to infer ContentBlock type: missing 'type' discriminator and no known properties found.");
    }

    public override void Write(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
    {
        // When we write, we always include the 'type' discriminator for strict Anthropic compatibility.
        switch (value)
        {
            case TextBlock tb:
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", tb.Text);
                if (tb.CacheControl is not null)
                {
                    writer.WritePropertyName("cache_control");
                    tb.CacheControl.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
                return;

            case ImageBlock ib:
                writer.WriteStartObject();
                writer.WriteString("type", "image");
                writer.WritePropertyName("source");
                JsonSerializer.Serialize(writer, ib.Source, options);
                writer.WriteEndObject();
                return;

            case ToolUseBlock tu:
                writer.WriteStartObject();
                writer.WriteString("type", "tool_use");
                writer.WriteString("id", tu.Id);
                writer.WriteString("name", tu.Name);
                writer.WritePropertyName("input");
                tu.Input.WriteTo(writer);
                writer.WriteEndObject();
                return;

            case ToolResultBlock tr:
                writer.WriteStartObject();
                writer.WriteString("type", "tool_result");
                writer.WriteString("tool_use_id", tr.ToolUseId);
                writer.WriteString("content", tr.Content);
                if (tr.IsError is not null)
                    writer.WriteBoolean("is_error", tr.IsError.Value);
                writer.WriteEndObject();
                return;

            case ThinkingBlock th:
                writer.WriteStartObject();
                writer.WriteString("type", "thinking");
                writer.WriteString("thinking", th.Thinking);
                writer.WriteEndObject();
                return;
        }

        JsonSerializer.Serialize(writer, (object)value, options);
    }
}

