namespace AgentRouter.Contracts.Defaults;

/// <summary>Well-known default values shared by the proxy, panel, and CLI.</summary>
public static class GatewayDefaults
{
    public const int ProxyPort = 49250;

    public const int PanelPort = 6767;

    public const string LoopbackHost = "127.0.0.1";

    /// <summary>Environment variable that overrides the bind interface (Docker sets 0.0.0.0).</summary>
    public const string BindHostEnvironmentVariable = "CC_GATEWAY_BIND_HOST";

    /// <summary>Environment variable used by the Electron supervisor to inject the master key.</summary>
    public const string SecretKeyEnvironmentVariable = "CC_GATEWAY_SECRET_KEY";

    /// <summary>Environment variable overriding the gateway data directory.</summary>
    public const string ConfigDirectoryEnvironmentVariable = "CC_GATEWAY_CONFIG_DIR";

    /// <summary>Environment variable adding extra allowed panel origins (comma separated).</summary>
    public const string PanelOriginsEnvironmentVariable = "CC_GATEWAY_PANEL_ORIGINS";

    /// <summary>Prefix for every generated gateway auth token.</summary>
    public const string AuthTokenPrefix = "sk_";

    /// <summary>Maximum number of log rows retained in the ring table.</summary>
    public const int LogRingSize = 5000;

    /// <summary>Maximum number of archived sessions retained.</summary>
    public const int SessionArchiveLimit = 200;

    /// <summary>Prompt capture limit for the first request in a session.</summary>
    public const int FirstPromptCaptureBytes = 80 * 1024;

    /// <summary>Prompt capture limit for subsequent requests in a session.</summary>
    public const int SubsequentPromptCaptureBytes = 4 * 1024;

    /// <summary>Response capture limit stored for panel inspection.</summary>
    public const int ResponseCaptureBytes = 20 * 1024;

    /// <summary>Heartbeat timeout after which a session is considered abandoned.</summary>
    public static readonly TimeSpan SessionHeartbeatTimeout = TimeSpan.FromSeconds(60);
}
