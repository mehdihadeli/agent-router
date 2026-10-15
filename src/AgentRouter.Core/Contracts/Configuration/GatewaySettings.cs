using System.Text.Json.Serialization;

namespace AgentRouter.Core.Contracts.Configuration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GatewaySettings
{
    public int SchemaVersion { get; set; } = 1;
    public SettingsServer Server { get; set; } = new();
    public Dictionary<string, SettingsProvider> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public SettingsRouting Routing { get; set; } = new();
    public SettingsExecution Execution { get; set; } = new();
    public Dictionary<string, SettingsAgent> Agents { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsServer
{
    public int Port { get; set; } = 49250;
    public string Host { get; set; } = "127.0.0.1";
    public string? TokenEnvironment { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsProvider
{
    public bool Enabled { get; set; } = true;
    public ProviderCompatibility Protocol { get; set; } = ProviderCompatibility.OpenAi;
    public string Endpoint { get; set; } = "";
    public List<string> Models { get; set; } = [];
    public List<SettingsCredential> Credentials { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsCredential
{
    public string Id { get; set; } = "default";
    public bool Enabled { get; set; } = true;
    public string? Environment { get; set; }
    public string? Secret { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsRouting
{
    public string DefaultModel { get; set; } = "";
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SettingsRoute> Rules { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsRoute
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Agent { get; set; }
    public string? Task { get; set; }
    public string? Subagent { get; set; }
    public string? Workflow { get; set; }
    public string? RequestedModel { get; set; }
    public string? ContainsText { get; set; }
    public string Model { get; set; } = "";
    public SettingsRewrite Rewrite { get; set; } = new();
    public List<string> FallbackModels { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsRewrite
{
    public int? MaxOutputTokens { get; set; }
    public float? Temperature { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsExecution
{
    public int MaxAttempts { get; set; } = 3;
    public int DeadlineSeconds { get; set; } = 60;
    public int CredentialCooldownSeconds { get; set; } = 30;
    public List<string> FallbackModels { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SettingsAgent
{
    public string Kind { get; set; } = "claude";
    public string? Model { get; set; }
    public string? Executable { get; set; }
}
