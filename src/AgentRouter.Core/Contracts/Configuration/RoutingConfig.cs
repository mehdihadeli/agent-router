namespace AgentRouter.Contracts.Configuration;

using System.Text.Json.Serialization;

/// <summary>Tier-based routing overrides for Claude Code model selection.</summary>
public sealed class RoutingConfig
{
    /// <summary>Catch-all rule applied when no tier-specific rule matches.</summary>
    public RoutingRule Default { get; set; } = new();

    /// <summary>Overrides <c>claude-opus-*</c> requests.</summary>
    public RoutingRule Opus { get; set; } = new();

    /// <summary>Overrides <c>claude-sonnet-*</c> requests.</summary>
    public RoutingRule Sonnet { get; set; } = new();

    /// <summary>Overrides <c>claude-haiku-*</c> requests.</summary>
    public RoutingRule Haiku { get; set; } = new();

    /// <summary>Projects the four tiers keyed by <see cref="RoutingTier"/>.</summary>
    public IReadOnlyDictionary<RoutingTier, RoutingRule> AsMap() =>
        new Dictionary<RoutingTier, RoutingRule>
        {
            [RoutingTier.Default] = Default,
            [RoutingTier.Opus] = Opus,
            [RoutingTier.Sonnet] = Sonnet,
            [RoutingTier.Haiku] = Haiku,
        };
}

/// <summary>A single tier routing rule. Active only when enabled, with a provider and model set.</summary>
public sealed class RoutingRule
{
    public bool Enabled { get; set; }

    public string? ProviderId { get; set; }

    public string? Model { get; set; }

    /// <summary>True when the rule has everything it needs to take effect.</summary>
    [JsonIgnore]
    public bool IsActive =>
        Enabled && !string.IsNullOrWhiteSpace(ProviderId) && !string.IsNullOrWhiteSpace(Model);
}
