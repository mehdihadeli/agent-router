namespace AgentRouter.Contracts.Configuration;

using System.Text.Json.Serialization;

/// <summary>Configuration for a single provider, built-in or user-created.</summary>
public sealed class ProviderConfig
{
    public bool Enabled { get; set; }

    /// <summary>Which protocol the endpoint speaks. Determines which transport is used.</summary>
    public ProviderCompatibility Compatibility { get; set; } = ProviderCompatibility.OpenAi;

    public ProviderAuthType AuthType { get; set; } = ProviderAuthType.ApiKey;

    /// <summary>API key. Persisted in the encrypted secret store, blanked in panel reads.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Upstream base URL. Each built-in provider supplies its own default.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>User-curated model list. Empty means auto-discover every available model.</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>Model ids hidden from the catalog.</summary>
    public List<string> DisabledModels { get; set; } = new();

    /// <summary>Maximum request starts per <see cref="RateWindow"/>. Zero disables the limit.</summary>
    public int RateLimit { get; set; }

    /// <summary>Rate window in seconds. Zero disables the limit.</summary>
    public int RateWindow { get; set; }

    /// <summary>Maximum simultaneous in-flight streams. Zero disables the limit.</summary>
    public int MaxConcurrency { get; set; }

    /// <summary>OAuth token container. Present only for OAuth providers.</summary>
    public OAuthTokens? OAuth { get; set; }

    /// <summary>Present only for user-created custom providers.</summary>
    public ProviderCustomMetadata? Custom { get; set; }

    [JsonIgnore]
    public bool IsCustom => Custom is not null;
}

/// <summary>Metadata for a user-created custom provider.</summary>
public sealed class ProviderCustomMetadata
{
    /// <summary>Human-readable name shown in the panel.</summary>
    public string? Label { get; set; }

    /// <summary>Immutable slug used in the CLI (<c>ccg --&lt;slug&gt;</c>) and as the provider id.</summary>
    public string? Slug { get; set; }

    /// <summary>File name of the uploaded logo inside the provider-logos directory.</summary>
    public string? LogoFile { get; set; }
}

/// <summary>OAuth token container. Every value is stored encrypted.</summary>
public sealed class OAuthTokens
{
    public string? AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public string? CopilotToken { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? AccountId { get; set; }

    public string? AccountLabel { get; set; }
}
