namespace AgentRouter.Storage;

/// <summary>Overrides for storage location and master key resolution.</summary>
public sealed class StorageOptions
{
    /// <summary>Gateway data directory. Defaults to <c>CC_GATEWAY_CONFIG_DIR</c>, then the OS location.</summary>
    public string? Directory { get; set; }

    /// <summary>Explicit SQLite database path. Defaults to <c>gateway.sqlite</c> in the data directory.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Hex-encoded master key. Normally left unset so <c>CC_GATEWAY_SECRET_KEY</c> or the persisted
    /// key file is used.
    /// </summary>
    public string? MasterKey { get; set; }
}
