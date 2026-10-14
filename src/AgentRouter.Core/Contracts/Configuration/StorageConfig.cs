namespace AgentRouter.Core.Contracts.Configuration;

/// <summary>Local storage locations used by the gateway.</summary>
public sealed class StorageConfig
{
    /// <summary>
    /// SQLite database path. Relative paths are resolved from the AgentRouter data directory.
    /// </summary>
    public string? DatabasePath { get; set; }
}
