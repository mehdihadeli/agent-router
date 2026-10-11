using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRouter.Models.Anthropic;

public sealed class MessageContentConverter : JsonConverter<MessageContent>
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
            var blocks = JsonSerializer.Deserialize<List<ContentBlock>>(ref reader, options);
            return new MessageContent { Blocks = blocks };
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

        if (value.Blocks is not null)
        {
            JsonSerializer.Serialize(writer, value.Blocks, options);
            return;
        }

        writer.WriteNullValue();
    }
}
