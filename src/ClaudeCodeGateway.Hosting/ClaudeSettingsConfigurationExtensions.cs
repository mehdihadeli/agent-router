using Microsoft.Extensions.Configuration;

namespace ClaudeCodeGateway.Shared.Configuration;

public static class ClaudeSettingsConfigurationExtensions
{
    public static IConfigurationBuilder AddClaudeSettings(
        this IConfigurationBuilder builder,
        string? homeOverridePath = null
    )
    {
        builder.Add(new ClaudeSettingsConfigurationSource { HomeOverridePath = homeOverridePath });
        return builder;
    }

    /// <summary>
    /// Load gateway-specific config from ~/claude-code-gateway/config.json.
    /// </summary>
    public static IConfigurationBuilder AddClaudeCodeGatewayConfig(
        this IConfigurationBuilder builder,
        string? homeOverridePath = null,
        bool createIfMissing = true
    )
    {
        builder.Add(
            new ClaudeCodeGatewayConfigConfigurationSource
            {
                HomeOverridePath = homeOverridePath,
                CreateIfMissing = createIfMissing,
            }
        );
        return builder;
    }

    /// <summary>
    /// Load a local .env file (KEY=VALUE) as a low-precedence configuration source.
    /// This is useful for local dev; real environment variables still override this.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvFile(
        this IConfigurationBuilder builder,
        string relativePath = ".env",
        bool optional = true
    )
    {
        builder.Add(
            new DotEnvConfigurationSource { RelativePath = relativePath, Optional = optional }
        );
        return builder;
    }
}
