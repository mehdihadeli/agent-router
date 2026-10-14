using AgentRouter.Core.Contracts.Defaults;

namespace AgentRouter.Core.Contracts.Configuration;

/// <summary>
/// The persisted gateway configuration document. This is the single source of truth for all
/// three surfaces; <c>ClaudeGatewayOptions</c> in Core is a runtime projection of this document
/// for the existing provider-binding path.
/// </summary>
public sealed class GatewayConfigDocument
{
    public StorageConfig Storage { get; set; } = new();

    public ServerConfig Server { get; set; } = new();

    /// <summary>Per-provider configuration keyed by provider id or custom provider slug.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

    public RoutingConfig Routing { get; set; } = new();

    public ThinkingConfig Thinking { get; set; } = new();

    public WebToolsConfig WebTools { get; set; } = new();

    public OutboundProxyConfig Proxy { get; set; } = new();

    public TokenSaversConfig TokenSavers { get; set; } = new();

    /// <summary>The currently selected provider id.</summary>
    public string? ActiveProvider { get; set; }

    public ModelMode ModelMode { get; set; } = ModelMode.Single;

    /// <summary>Slug of the currently active model chain, or <see langword="null"/>.</summary>
    public string? ActiveModelFallbackSlug { get; set; }

    public List<ModelFallbackChain> ModelFallbacks { get; set; } = new();

    public PanelSettings PanelSettings { get; set; } = new();

    public List<AgentProfile> Profiles { get; set; } = new();

    /// <summary>Creates a document populated with gateway defaults.</summary>
    public static GatewayConfigDocument CreateDefault()
    {
        return new GatewayConfigDocument
        {
            Server = new ServerConfig
            {
                ProxyPort = GatewayDefaults.ProxyPort,
                PanelPort = GatewayDefaults.PanelPort,
            },
        };
    }
}
