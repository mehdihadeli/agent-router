using System.Text.Json;
using AgentRouter.Core.Storage;

namespace AgentRouter.Core.Hosting;

/// <summary>
/// Loads the user config file at ~/.agent-router/config.json unless the data directory is overridden.
///
/// Supported JSON schema:
/// - Flat (preferred): properties at root
///   { "GatewayBaseAddress": "http://...", "DefaultProviderType": "Azure", ... }
///
/// Strings support $VAR and ${VAR} env interpolation. Arrays are stored as raw JSON.
/// </summary>
public sealed class AgentRouterConfigConfigurationProvider : ConfigurationProvider
{
    private readonly string _configPath;
    private readonly bool _createIfMissing;

    public AgentRouterConfigConfigurationProvider(string? homeOverridePath, bool createIfMissing)
    {
        var rootDirectory = string.IsNullOrWhiteSpace(homeOverridePath)
            ? GatewayPaths.Resolve().RootDirectory
            : Path.Combine(homeOverridePath, ".agent-router");

        _configPath = Path.Combine(rootDirectory, "config.json");
        _createIfMissing = createIfMissing;
    }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_configPath))
        {
            if (_createIfMissing)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.WriteAllText(_configPath, DefaultConfigTemplate);
            }
            else
            {
                return;
            }
        }

        using var stream = File.OpenRead(_configPath);
        using var doc = JsonDocument.Parse(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return;

        // Flat root only
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            FlattenJsonElement(prop.Value, prop.Name);
        }
    }

    private void FlattenJsonElement(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    FlattenJsonElement(p.Value, ConfigurationPath.Combine(path, p.Name));
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJsonElement(item, ConfigurationPath.Combine(path, index.ToString()));
                    index++;
                }

                break;
            case JsonValueKind.String:
                Data[path] = ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(
                    element.GetString()
                );
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                Data[path] = element.ToString();
                break;
            default:
                Data[path] = element.GetRawText();
                break;
        }
    }

    private static readonly string DefaultConfigTemplate =
        "{\n"
        + "  \"EnableTelemetry\": true,\n"
        + "  \"GatewayBaseAddress\": \"http://localhost:4000\"\n"
        + "}\n";
}
