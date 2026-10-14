using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Storage.Secrets;

namespace AgentRouter.Core.Storage.Configuration;

/// <summary>
/// Moves secret values between the configuration document and the encrypted secret store, and
/// produces the masked view returned by the panel.
/// </summary>
public sealed class ConfigSecretSplitter
{
    private readonly ISecretStore _secretStore;

    public ConfigSecretSplitter(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    /// <summary>Copies persisted secrets back into the document so callers see usable values.</summary>
    public async Task<GatewayConfigDocument> HydrateAsync(
        GatewayConfigDocument document,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (providerId, provider) in document.Providers)
        {
            provider.ApiKey = await _secretStore
                .GetAsync(SecretKeys.ProviderApiKey(providerId), cancellationToken)
                .ConfigureAwait(false);

            if (provider.OAuth is null)
            {
                continue;
            }

            provider.OAuth.AccessToken = await _secretStore
                .GetAsync(SecretKeys.ProviderOAuthAccessToken(providerId), cancellationToken)
                .ConfigureAwait(false);
            provider.OAuth.RefreshToken = await _secretStore
                .GetAsync(SecretKeys.ProviderOAuthRefreshToken(providerId), cancellationToken)
                .ConfigureAwait(false);
            provider.OAuth.CopilotToken = await _secretStore
                .GetAsync(SecretKeys.ProviderOAuthCopilotToken(providerId), cancellationToken)
                .ConfigureAwait(false);
        }

        return document;
    }

    /// <summary>
    /// Writes secret values to the encrypted store and returns a detached copy with every secret
    /// blanked, so the serialized copy never contains plaintext. The caller's document keeps its
    /// usable values.
    /// </summary>
    public async Task<GatewayConfigDocument> ExtractAsync(
        GatewayConfigDocument document,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        var stripped = Clone(document);

        await _secretStore
            .SetAsync(SecretKeys.ServerAuthToken, stripped.Server.AuthToken, cancellationToken)
            .ConfigureAwait(false);
        stripped.Server.AuthToken = null;

        foreach (var (providerId, provider) in stripped.Providers)
        {
            await _secretStore
                .SetAsync(SecretKeys.ProviderApiKey(providerId), provider.ApiKey, cancellationToken)
                .ConfigureAwait(false);
            provider.ApiKey = null;

            if (provider.OAuth is null)
            {
                continue;
            }

            await _secretStore
                .SetAsync(
                    SecretKeys.ProviderOAuthAccessToken(providerId),
                    provider.OAuth.AccessToken,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await _secretStore
                .SetAsync(
                    SecretKeys.ProviderOAuthRefreshToken(providerId),
                    provider.OAuth.RefreshToken,
                    cancellationToken
                )
                .ConfigureAwait(false);
            await _secretStore
                .SetAsync(
                    SecretKeys.ProviderOAuthCopilotToken(providerId),
                    provider.OAuth.CopilotToken,
                    cancellationToken
                )
                .ConfigureAwait(false);

            provider.OAuth = null;
        }

        return stripped;
    }

    /// <summary>
    /// Returns a detached copy safe to send to the panel: the auth token is blanked and API keys
    /// are reduced to a preview. Masked values sent back are ignored by the store.
    /// </summary>
    public static GatewayConfigDocument Mask(GatewayConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var copy = Clone(document);
        copy.Server.AuthToken = null;

        foreach (var provider in copy.Providers.Values)
        {
            provider.ApiKey = MaskSecret(provider.ApiKey);

            if (provider.OAuth is null)
            {
                continue;
            }

            provider.OAuth.AccessToken = MaskSecret(provider.OAuth.AccessToken);
            provider.OAuth.RefreshToken = MaskSecret(provider.OAuth.RefreshToken);
            provider.OAuth.CopilotToken = MaskSecret(provider.OAuth.CopilotToken);
        }

        return copy;
    }

    /// <summary>Reduces a secret to a display preview such as <c>••••a1b2</c>.</summary>
    public static string? MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Contains(AesGcmSecretStore.MaskToken, StringComparison.Ordinal))
        {
            return value;
        }

        const int visibleSuffixLength = 4;

        return value.Length <= visibleSuffixLength
            ? AesGcmSecretStore.MaskToken
            : AesGcmSecretStore.MaskToken + value[^visibleSuffixLength..];
    }

    /// <summary>Repairs null collections introduced by hand-edited or legacy config payloads.</summary>
    public static GatewayConfigDocument Normalize(GatewayConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Providers ??= new Dictionary<string, ProviderConfig>();
        document.ModelFallbacks ??= new List<ModelFallbackChain>();
        document.Routing ??= new RoutingConfig();
        document.Thinking ??= new ThinkingConfig();
        document.WebTools ??= new WebToolsConfig();
        document.Proxy ??= new OutboundProxyConfig();
        document.TokenSavers ??= new TokenSaversConfig();
        document.PanelSettings ??= new PanelSettings();
        document.PanelSettings.FavoriteProviders ??= new List<string>();

        foreach (var provider in document.Providers.Values)
        {
            provider.Models ??= new List<string>();
            provider.DisabledModels ??= new List<string>();
        }

        foreach (var chain in document.ModelFallbacks)
        {
            chain.Models ??= new List<ModelFallbackTarget>();
        }

        return document;
    }

    private static GatewayConfigDocument Clone(GatewayConfigDocument document)
    {
        var json = JsonSerializer.Serialize(document, GatewayJson.Options);

        return JsonSerializer.Deserialize<GatewayConfigDocument>(json, GatewayJson.Options)
            ?? GatewayConfigDocument.CreateDefault();
    }
}
