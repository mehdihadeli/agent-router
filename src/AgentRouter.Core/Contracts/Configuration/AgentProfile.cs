namespace AgentRouter.Core.Contracts.Configuration;

public sealed class AgentProfile
{
    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Model { get; set; }

    public string Kind { get; set; } = "claude";

    /// <summary>Where this profile may be opened: cli, app, or cli-and-app.</summary>
    public string EntryMode { get; set; } = "cli";

    /// <summary>Whether the profile changes only child launches or is a system default.</summary>
    public string Scope { get; set; } = "launch";

    /// <summary>Optional Claude tier selectors keyed by opus, sonnet, or haiku.</summary>
    public Dictionary<string, string> TierModels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? TelemetryEndpoint { get; set; }

    /// <summary>Optional non-gateway environment overrides for the child process.</summary>
    public Dictionary<string, string> Environment { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional absolute working directory for the child process.</summary>
    public string? WorkingDirectory { get; set; }

    public string Executable { get; set; } = "claude";
}
