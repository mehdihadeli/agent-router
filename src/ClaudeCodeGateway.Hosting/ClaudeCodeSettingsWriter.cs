using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeCodeGateway.Shared.Configuration;

public static class ClaudeCodeSettingsWriter
{
    public static void EnsureClaudeCodeSettings(ClaudeGatewayOptions opts)
    {
        var gatewayAddress = opts.GatewayBaseAddress;

        // Claude Code expects these env vars. RouteModel is mandatory in our config.
        ArgumentNullException.ThrowIfNull(opts.RouteModel);
        var defaultModel = opts.RouteModel.DefaultModel;
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModel);

        var home =
            Environment.GetEnvironmentVariable("USERPROFILE")
            ?? Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dir = Path.Combine(home, ".claude");
        var path = Path.Combine(dir, "settings.json");

        Directory.CreateDirectory(dir);

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var env = root["env"] as JsonObject ?? new JsonObject();
        env["ANTHROPIC_BASE_URL"] = gatewayAddress;
        env["ANTHROPIC_AUTH_TOKEN"] = "default";
        env["ANTHROPIC_DEFAULT_OPUS_MODEL"] = defaultModel;
        env["ANTHROPIC_DEFAULT_SONNET_MODEL"] = defaultModel;
        env["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = defaultModel;
        env["CLAUDE_CODE_GIT_BASH_PATH"] =
            opts.GitBashPath ?? "C:\\Program Files\\Git\\bin\\bash.exe";
        root["env"] = env;
        root["autoUpdatesChannel"] ??= "latest";

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        File.WriteAllText(path, root.ToJsonString(options));
    }
}
