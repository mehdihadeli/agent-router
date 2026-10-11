using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRouter.Contracts.Serialization;

/// <summary>
/// Single source of JSON conventions for every gateway surface (config document, storage
/// payloads, panel API). Keeping this in one place prevents drift between the proxy and the
/// panel, which is a documented weakness of the reference implementation.
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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
