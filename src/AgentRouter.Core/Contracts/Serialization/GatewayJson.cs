using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRouter.Core.Contracts.Configuration;

namespace AgentRouter.Core.Contracts.Serialization;

/// <summary>
/// Shared JSON conventions for AgentRouter configuration, storage, and management output.
/// </summary>
public static class GatewayJson
{
    /// <summary>Compact options used for persisted payloads and wire responses.</summary>
    public static JsonSerializerOptions Options { get; } = Create(indented: false);

    /// <summary>Indented options used for human-readable exports and config files.</summary>
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new LegacyEnumConverter<ProviderAuthType>());
        options.Converters.Add(new LegacyEnumConverter<RoutingStrategy>());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private sealed class LegacyEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            if (
                reader.TokenType == JsonTokenType.String
                && Enum.TryParse<TEnum>(
                    reader.GetString()?.Replace("_", "", StringComparison.Ordinal),
                    true,
                    out var value
                )
            )
                return value;
            throw new JsonException("Invalid configuration enum value.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            TEnum value,
            JsonSerializerOptions options
        )
        {
            if (Enum.GetName(value) is { } name)
                writer.WriteStringValue(name);
            else
                writer.WriteNumberValue(
                    Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
                );
        }
    }
}
