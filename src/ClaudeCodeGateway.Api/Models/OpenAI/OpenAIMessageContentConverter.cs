using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeGateway.Models.OpenAI;

public sealed class OpenAIMessageContentConverter : JsonConverter<MessageContent>
{
    public override MessageContent? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return new MessageContent { Text = text };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var parts = JsonSerializer.Deserialize<List<ContentPart>>(ref reader, options);
            return new MessageContent { Parts = parts };
        }

        throw new JsonException("MessageContent must be string or array.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        MessageContent value,
        JsonSerializerOptions options
    )
    {
        if (value.Text is not null)
        {
            writer.WriteStringValue(value.Text);
            return;
        }

        if (value.Parts is not null)
        {
            JsonSerializer.Serialize(writer, value.Parts, options);
            return;
        }

        writer.WriteNullValue();
    }
}
