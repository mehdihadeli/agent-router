using Microsoft.Extensions.Configuration;

namespace AgentRouter.Shared.Configuration;

/// <summary>
/// Minimal .env loader (KEY=VALUE) with $VAR / ${VAR} interpolation on values.
/// Lines starting with # are ignored. Quotes are trimmed ("value" or 'value').
/// </summary>
public sealed class DotEnvConfigurationProvider(string fullPath, bool optional)
    : ConfigurationProvider
{
    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(fullPath))
        {
            if (optional)
                return;
            throw new FileNotFoundException(".env file not found", fullPath);
        }

        foreach (var rawLine in File.ReadAllLines(fullPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var idx = line.IndexOf('=');
            if (idx <= 0)
                continue;

            var key = line.Substring(0, idx).Trim().Replace("__", ":", StringComparison.Ordinal);
            var value = line.Substring(idx + 1).Trim();

            value = TrimQuotes(value);
            value =
                ClaudeSettingsConfigurationProvider.InterpolateEnvironmentVariables(value) ?? value;

            if (key.Length == 0)
                continue;

            Data[key] = value;
        }
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                return value.Substring(1, value.Length - 2);
        }

        return value;
    }
}
