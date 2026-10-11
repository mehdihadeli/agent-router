using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace AgentRouter.Shared.Configuration;

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
    /// Load gateway-specific config from ~/agent-router/config.json.
    /// </summary>
    public static IConfigurationBuilder AddAgentRouterConfig(
        this IConfigurationBuilder builder,
        string? homeOverridePath = null,
        bool createIfMissing = true
    )
    {
        builder.Add(
            new AgentRouterConfigConfigurationSource
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
        var source = new DotEnvConfigurationSource
        {
            RelativePath = relativePath,
            Optional = optional,
        };

        // Keep .env below environment variables (its documented contract) by inserting it
        // immediately before the first env-var source. Appending would let a stray local .env
        // silently override env vars and test configuration.
        var sources = builder.Sources;
        var envVarIndex = IndexOfEnvironmentVariablesSource(sources);

        if (envVarIndex >= 0)
        {
            sources.Insert(envVarIndex, source);
        }
        else
        {
            builder.Add(source);
        }

        return builder;
    }

    private static int IndexOfEnvironmentVariablesSource(IList<IConfigurationSource> sources)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                return i;
            }
        }

        return -1;
    }
}
