using Common.AIProviders;

namespace Common.Routes;

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

        // Find provider by name (preferred) and fall back to ProviderType name match.
        ClaudeGatewayProvider? provider = options.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderType.ToString(),
                providerName,
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"No provider named '{providerName}' is configured. Configure it under 'Providers' (ProviderName/ProviderType)."
            );
        }

        var effectiveProviderName = provider.ProviderType.ToString().ToLowerInvariant();

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
        return $"{providerType.ToString().ToLowerInvariant()}/{model}";
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
            return Common.Constants.RouteModelKeys.Default;

        var parts = modelRoute.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
            return Common.Constants.RouteModelKeys.Default;

        var providerName = parts[0];
        var model        = parts[1];

        // Verify the provider is configured so we don't hand out a key that has no registration.
        var providerExists = options.Providers.Any(p =>
            string.Equals(p.ProviderType.ToString(), providerName, StringComparison.OrdinalIgnoreCase));

        if (!providerExists)
            return Common.Constants.RouteModelKeys.Default;

        // Return the normalised lowercase key, matching what GetProviderModelName produces.
        return $"{providerName.ToLowerInvariant()}/{model}";
    }

    public static ProviderType ParseProviderType(string providerName)
    {
        if (Enum.TryParse<ProviderType>(providerName, ignoreCase: true, out var pt))
            return pt;

        throw new ArgumentException(
            $"Unknown provider type '{providerName}'.",
            nameof(providerName)
        );
    }
}
