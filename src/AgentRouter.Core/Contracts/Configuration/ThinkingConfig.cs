namespace AgentRouter.Contracts.Configuration;

/// <summary>
/// Extended-thinking controls. A <see langword="null"/> tier value means "let the provider
/// decide"; a non-null value forces the flag on or off for that tier.
/// </summary>
public sealed class ThinkingConfig
{
    public bool Enabled { get; set; } = true;

    public bool? Opus { get; set; }

    public bool? Sonnet { get; set; }

    public bool? Haiku { get; set; }

    /// <summary>Resolves the effective thinking flag for a tier.</summary>
    public bool Resolve(RoutingTier tier) =>
        tier switch
        {
            RoutingTier.Opus => Opus ?? Enabled,
            RoutingTier.Sonnet => Sonnet ?? Enabled,
            RoutingTier.Haiku => Haiku ?? Enabled,
            _ => Enabled,
        };
}
