using AgentRouter.Core.Constants;
using AgentRouter.Core.Contracts;

namespace AgentRouter.Core.Routes;

public static class RouteModelFactory
{
    /// <summary>
    /// Parses model routes like "azure/gpt-4.1" into a provider + model reference and validates it exists.
    /// </summary>
    public static ModelReference Resolve(ClaudeGatewayOptions options, string modelRoute)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelRoute);

        var parts = modelRoute.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
        if (parts.Length != 2)
        {
            throw new ArgumentException(
                $"Invalid  modelRoute '{modelRoute}'. Expected format 'provider/model' (e.g. 'azure/gpt-4.1').",
                nameof(modelRoute)
            );
        }

        var providerName = parts[0].Trim();
        var model = parts[1].Trim();

        if (providerName.Length == 0 || model.Length == 0)
        {
            throw new ArgumentException(
                $"Invalid modelRoute '{modelRoute}'. Expected format 'provider/model' (e.g. 'azure/gpt-4.1').",
                nameof(modelRoute)
            );
        }

        // Find provider by configured type name or known aliases (e.g. github -> GitHubModels).
        ClaudeGatewayProvider? provider = options.Providers.FirstOrDefault(p =>
            IsProviderNameMatch(p, providerName)
        );

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"No provider named '{providerName}' is configured. Configure it under 'Providers' (ProviderName/ProviderType)."
            );
        }

        var effectiveProviderName = GetProviderRouteName(provider);

        if (provider.Models is null || provider.Models.Length == 0)
        {
            throw new InvalidOperationException(
                $"Provider '{effectiveProviderName}' has no models configured. Add at least one entry to Providers[].Models."
            );
        }

        if (!provider.Models.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Model '{model}' is not configured for provider '{effectiveProviderName}'. Allowed: {string.Join(", ", provider.Models)}"
            );
        }

        // Determine enum provider type from provider itself.
        var providerType = provider.ProviderType;

        // Normalize model to the configured casing (nice for metadata/logging).
        var normalizedModel = provider.Models.First(m =>
            string.Equals(m, model, StringComparison.OrdinalIgnoreCase)
        );

        return new ModelReference(
            Name: effectiveProviderName,
            ProviderType: providerType,
            Model: normalizedModel,
            Provider: provider
        );
    }

    /// <summary>
    /// Builds the canonical service key used to register / resolve a keyed <c>IChatClient</c>.
    /// Format: <c>{providerName}/{model}</c>, e.g. <c>"azure/gpt-4.1"</c>.
    /// </summary>
    public static string GetProviderModelName(ProviderType providerType, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return $"{GetProviderRouteName(providerType)}/{model}";
    }

    public static string GetProviderModelName(ClaudeGatewayProvider provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return $"{GetProviderRouteName(provider)}/{model}";
    }

    public static ModelReference ResolveRequestModel(
        ClaudeGatewayOptions options,
        string? modelRoute
    )
    {
        if (!string.IsNullOrWhiteSpace(modelRoute))
        {
            var parts = modelRoute.Split('/', 2, StringSplitOptions.TrimEntries);
            var provider = options.Providers.FirstOrDefault(candidate =>
                parts.Length == 2
                && IsProviderNameMatch(candidate, parts[0])
                && candidate.Models.Any(model =>
                    string.Equals(parts[1], model, StringComparison.OrdinalIgnoreCase)
                )
            );
            if (provider is not null)
            {
                return Resolve(options, modelRoute);
            }

            foreach (var tier in options.TierRoutes)
            {
                if (
                    modelRoute.StartsWith($"claude-{tier.Key}-", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return Resolve(options, tier.Value);
                }
            }
        }

        return Resolve(options, options.RouteModel.DefaultModel);
    }

    /// <summary>
    /// Given a raw model string from a Claude Code request (e.g. <c>"azure/gpt-4.1"</c>,
    /// <c>"claude-sonnet-4-5"</c>, or <c>null</c>) returns the service key to use when
    /// resolving a keyed <c>IChatClient</c>.
    /// <list type="bullet">
    ///   <item><c>"provider/model"</c> — returned as-is (lowercased) after validating the provider exists in <paramref name="options"/>.</item>
    ///   <item>No slash / unknown — falls back to <c>"default"</c>.</item>
    ///   <item><c>null</c> / empty — falls back to <c>"default"</c>.</item>
    /// </list>
    /// </summary>
    public static string ResolveServiceKey(ClaudeGatewayOptions options, string? modelRoute)
    {
        if (string.IsNullOrWhiteSpace(modelRoute))
            return RouteModelKeys.Default;

        var parts = modelRoute.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length != 2)
            return RouteModelKeys.Default;

        var providerName = parts[0];
        var model = parts[1];

        // Verify the provider is configured so we don't hand out a key that has no registration.
        var provider = options.Providers.FirstOrDefault(p => IsProviderNameMatch(p, providerName));

        if (provider is null)
            return RouteModelKeys.Default;

        // Return the normalised lowercase key, matching what GetProviderModelName produces.
        return GetProviderModelName(provider, model);
    }

    /// <summary>
    /// Returns the model name to forward to the provider for a gateway route.
    /// <list type="bullet">
    ///   <item><c>"openai/deepseek-flash"</c> when <c>openai</c> is a configured provider → <c>"deepseek-flash"</c>.</item>
    ///   <item>A route whose first segment is not a configured provider → returned unchanged.</item>
    ///   <item>No slash (or null/empty) → returned unchanged.</item>
    /// </list>
    /// Only the first segment is treated as the provider, so model names that legitimately
    /// contain a slash (e.g. <c>"openai/meta-llama/Llama-3-70B"</c>) survive intact.
    /// </summary>
    public static string GetProviderModelId(ClaudeGatewayOptions options, string? modelRoute)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(modelRoute))
            return modelRoute ?? string.Empty;

        var parts = modelRoute.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return modelRoute;

        var providerIsConfigured = options.Providers.Any(p => IsProviderNameMatch(p, parts[0]));

        return providerIsConfigured ? parts[1] : modelRoute;
    }

    public static ProviderType ParseProviderType(string providerName)
    {
        if (
            string.Equals(providerName, "github", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerName, "githubmodels", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerName, "github-models", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ProviderType.GitHubModels;
        }

        if (Enum.TryParse<ProviderType>(providerName, ignoreCase: true, out var pt))
            return pt;

        throw new ArgumentException(
            $"Unknown provider type '{providerName}'.",
            nameof(providerName)
        );
    }

    private static bool IsProviderNameMatch(ClaudeGatewayProvider provider, string providerName)
    {
        if (!string.IsNullOrWhiteSpace(provider.ProviderName))
            return string.Equals(
                provider.ProviderName,
                providerName,
                StringComparison.OrdinalIgnoreCase
            );

        var providerType = provider.ProviderType;
        if (
            string.Equals(providerType.ToString(), providerName, StringComparison.OrdinalIgnoreCase)
        )
            return true;

        return string.Equals(
            GetProviderRouteName(providerType),
            providerName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string GetProviderRouteName(ClaudeGatewayProvider provider) =>
        string.IsNullOrWhiteSpace(provider.ProviderName)
            ? GetProviderRouteName(provider.ProviderType)
            : provider.ProviderName.ToLowerInvariant();

    private static string GetProviderRouteName(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.GitHubModels => "github",
            _ => providerType.ToString().ToLowerInvariant(),
        };
    }
}
