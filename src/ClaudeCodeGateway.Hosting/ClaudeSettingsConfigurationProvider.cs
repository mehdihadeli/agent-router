using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ClaudeCodeGateway.Shared.Configuration;

public sealed class ClaudeSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly string _settingsPath;

    public ClaudeSettingsConfigurationProvider(string? homeOverridePath = null)
    {
        var home = homeOverridePath;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        _settingsPath = Path.Combine(home, ".claude", "settings.json");
    }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_settingsPath))
            return;

        using var stream = File.OpenRead(_settingsPath);
        using var doc = JsonDocument.Parse(stream);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return;

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
                // To keep it simple/stable with standard configuration binder, serialize arrays as JSON.
                // This still allows binding to JsonElement/string and custom parsing downstream.
                Data[path] = InterpolateEnvironmentVariables(element.GetRawText());
                break;
            case JsonValueKind.String:
                Data[path] = InterpolateEnvironmentVariables(element.GetString());
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

    public static string? InterpolateEnvironmentVariables(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Support $VAR and ${VAR}
        // Keep placeholders intact when variable isn't set.
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            "\\$(?:\\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\\}|(?<name>[A-Za-z_][A-Za-z0-9_]*))",
            m =>
            {
                var name = m.Groups["name"].Value;
                var env = Environment.GetEnvironmentVariable(name);
                return env ?? m.Value;
            }
        );
    }
}
