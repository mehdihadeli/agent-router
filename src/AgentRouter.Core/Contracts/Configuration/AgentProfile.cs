namespace AgentRouter.Contracts.Configuration;

public sealed class AgentProfile
{
    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Model { get; set; }

    public string Executable { get; set; } = "claude";
}
