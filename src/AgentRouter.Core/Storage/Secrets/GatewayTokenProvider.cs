using System.Security.Cryptography;
using AgentRouter.Core.Contracts.Defaults;

namespace AgentRouter.Core.Storage.Secrets;

/// <summary>Provides and rotates the gateway auth token used to authenticate local requests.</summary>
public interface IGatewayTokenProvider
{
    /// <summary>Returns the stored token, generating and persisting one on first use.</summary>
    Task<string> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates a new token, replaces the stored one, and returns it.</summary>
    Task<string> RotateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IGatewayTokenProvider"/> backed by the encrypted secret store.</summary>
public sealed class GatewayTokenProvider : IGatewayTokenProvider
{
    private const int TokenEntropyBytes = 16;

    private readonly ISecretStore _secretStore;

    public GatewayTokenProvider(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _secretStore
            .GetAsync(SecretKeys.ServerAuthToken, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return await RotateAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RotateAsync(CancellationToken cancellationToken = default)
    {
        var token =
            GatewayDefaults.AuthTokenPrefix
            + Convert
                .ToHexString(RandomNumberGenerator.GetBytes(TokenEntropyBytes))
                .ToLowerInvariant();

        await _secretStore
            .SetAsync(SecretKeys.ServerAuthToken, token, cancellationToken)
            .ConfigureAwait(false);

        return token;
    }
}
