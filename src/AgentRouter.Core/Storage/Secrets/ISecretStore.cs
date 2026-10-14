namespace AgentRouter.Core.Storage.Secrets;

/// <summary>
/// Encrypted key/value storage for API keys, OAuth tokens, and the gateway auth token.
/// Implementations must never return plaintext to logs and must treat undecryptable entries as
/// missing rather than failing the request.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the plaintext value, or <see langword="null"/> when absent or unreadable.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value. A <see langword="null"/>, empty, or mask-containing value is ignored so a
    /// round-tripped masked payload cannot erase a secret. Use <see cref="RemoveAsync"/> to delete.
    /// </summary>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Returns every readable entry, skipping any that cannot be decrypted.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        CancellationToken cancellationToken = default
    );
}
