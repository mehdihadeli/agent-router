namespace AgentRouter.Contracts.Configuration;

/// <summary>Web search tool availability and network access policy.</summary>
public sealed class WebToolsConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>When false, web tools refuse to reach private network ranges.</summary>
    public bool AllowPrivateNetworks { get; set; }
}

/// <summary>Outbound HTTP/HTTPS proxy used for upstream provider calls.</summary>
public sealed class OutboundProxyConfig
{
    public bool Enabled { get; set; }

    public string? Url { get; set; }
}

/// <summary>Token saver toggles.</summary>
public sealed class TokenSaversConfig
{
    /// <summary>Enables RTK compression of large tool results (reduces input tokens).</summary>
    public bool RtkEnabled { get; set; }

    /// <summary>Enables the Caveman terse-output prompt injection.</summary>
    public bool CavemanEnabled { get; set; }

    public CavemanLevel CavemanLevel { get; set; } = CavemanLevel.Lite;
}
