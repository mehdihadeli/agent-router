namespace AgentRouter.Core.Storage;

/// <summary>Overrides for storage location and master key resolution.</summary>
public sealed class StorageOptions
{
    /// <summary>Gateway data directory. Defaults to <c>AR_CONFIG_DIR</c>, then ~/.agent-router.</summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Explicit SQLite database path. Overrides <c>Storage.DatabasePath</c> in config.json and
    /// defaults to <c>gateway.sqlite</c> in the data directory.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Hex-encoded master key. Normally left unset so <c>AR_SECRET_KEY</c> or the persisted
    /// key file is used.
    /// </summary>
    public string? MasterKey { get; set; }
}
