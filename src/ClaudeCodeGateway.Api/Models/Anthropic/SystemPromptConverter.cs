using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeGateway.Models.Anthropic;

public sealed class SystemPromptConverter : JsonConverter<SystemPrompt>
{
    public override SystemPrompt? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return new SystemPrompt { Single = value };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var messages = JsonSerializer.Deserialize<List<SystemMessage>>(ref reader, options);
            return new SystemPrompt { Multiple = messages };
        }

        throw new JsonException("SystemPrompt must be string or array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        SystemPrompt value,
        JsonSerializerOptions options
    )
    {
        if (value.Single is not null)
        {
            writer.WriteStringValue(value.Single);
            return;
        }

        if (value.Multiple is not null)
        {
            JsonSerializer.Serialize(writer, value.Multiple, options);
            return;
        }

        writer.WriteNullValue();
    }
}
