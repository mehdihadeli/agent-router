using System.Text.Json.Serialization;

namespace AgentRouter.Contracts.Configuration;

/// <summary>Which wire protocol a provider endpoint speaks.</summary>
public enum ProviderCompatibility
{
    [JsonStringEnumMemberName("openai")]
    OpenAi,

    [JsonStringEnumMemberName("anthropic")]
    Anthropic,
}

/// <summary>How a provider authenticates upstream requests.</summary>
public enum ProviderAuthType
{
    [JsonStringEnumMemberName("api_key")]
    ApiKey,

    [JsonStringEnumMemberName("oauth")]
    OAuth,
}

/// <summary>How the model catalog presented to Claude Code is assembled.</summary>
public enum ModelMode
{
    /// <summary>Only the active provider's models (optionally narrowed to the active chain).</summary>
    Single,

    /// <summary>Every enabled provider plus every enabled model chain.</summary>
    All,

    /// <summary>Only enabled model chains.</summary>
    Chains,
}

/// <summary>Claude Code model tiers that can be overridden by tier routing rules.</summary>
public enum RoutingTier
{
    Default,
    Opus,
    Sonnet,
    Haiku,
}

/// <summary>How the first target of a model chain is selected.</summary>
public enum RoutingStrategy
{
    Waterfall,

    [JsonStringEnumMemberName("round_robin")]
    RoundRobin,
}

/// <summary>Verbosity level for the Caveman token saver.</summary>
public enum CavemanLevel
{
    Lite,
    Full,
    Ultra,
}
