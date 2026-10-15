using AgentRouter.Core.Contracts.Defaults;

namespace AgentRouter.Core.Contracts.Configuration;

/// <summary>Network settings for the proxy and panel listeners.</summary>
public sealed class ServerConfig
{
    public int ProxyPort { get; set; } = GatewayDefaults.ProxyPort;

    public int PanelPort { get; set; } = GatewayDefaults.PanelPort;

    /// <summary>
    /// Gateway auth token. Persisted in the encrypted secret store, never in the config document
    /// on disk, and always blanked in panel <c>GET /api/config</c> responses.
    /// </summary>
    public string? AuthToken { get; set; }
}
