using System.Security.Cryptography;
using System.Text;
using AgentRouter.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AgentRouter.Storage.Secrets;

/// <summary>
/// AES-256-GCM secret store backed by the <c>secret_entries</c> table. Every write uses a fresh
/// random nonce. Entries that fail to authenticate (for example after the master key changed) are
/// reported as missing instead of throwing.
/// </summary>
public sealed class AesGcmSecretStore : ISecretStore
{
    /// <summary>Marker used by the panel to mean "leave the stored secret unchanged".</summary>
    public const string MaskToken = "••••";

    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MasterKey _masterKey;
    private readonly ILogger<AesGcmSecretStore> _logger;

    public AesGcmSecretStore(
        SqliteConnectionFactory connectionFactory,
        MasterKey masterKey,
        ILogger<AesGcmSecretStore> logger
    )
    {
        _connectionFactory = connectionFactory;
        _masterKey = masterKey;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT nonce, ciphertext, tag FROM secret_entries WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var nonce = reader.GetString(0);
        var ciphertext = reader.GetString(1);
        var tag = reader.GetString(2);

        var plaintext = Decrypt(key, nonce, ciphertext, tag);

        if (plaintext is null)
        {
            _logger.LogWarning(
                "Secret entry '{SecretKey}' could not be decrypted and is treated as missing.",
                key
            );
        }

        return plaintext;
    }

    public async Task SetAsync(
        string key,
        string? value,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (string.IsNullOrEmpty(value) || value.Contains(MaskToken, StringComparison.Ordinal))
        {
            return;
        }

        var (nonce, ciphertext, tag) = Encrypt(value);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO secret_entries (key, nonce, ciphertext, tag, updated_at)
            VALUES ($key, $nonce, $ciphertext, $tag, $updatedAt)
            ON CONFLICT (key) DO UPDATE SET
                nonce = excluded.nonce,
                ciphertext = excluded.ciphertext,
                tag = excluded.tag,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$nonce", nonce);
        command.Parameters.AddWithValue("$ciphertext", ciphertext);
        command.Parameters.AddWithValue("$tag", tag);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM secret_entries WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM secret_entries WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return result is not null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, nonce, ciphertext, tag FROM secret_entries;";

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var plaintext = Decrypt(
                key,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            );

            if (plaintext is not null)
            {
                result[key] = plaintext;
            }
        }

        return result;
    }

    private (string Nonce, string Ciphertext, string Tag) Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_masterKey.Value, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return (ToHex(nonce), ToHex(ciphertext), ToHex(tag));
    }

    private string? Decrypt(string key, string nonceHex, string ciphertextHex, string tagHex)
    {
        try
        {
            var nonce = Convert.FromHexString(nonceHex);
            var ciphertext = Convert.FromHexString(ciphertextHex);
            var tag = Convert.FromHexString(tagHex);

            // AesGcm throws ArgumentException (not CryptographicException) for wrong nonce or tag
            // sizes, so validate up front to keep the "unreadable means missing" contract.
            if (nonce.Length != NonceSizeBytes || tag.Length != TagSizeBytes)
            {
                _logger.LogWarning(
                    "Secret entry '{SecretKey}' has an invalid nonce or tag length and is treated as missing.",
                    key
                );

                return null;
            }

            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_masterKey.Value, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            _logger.LogWarning(
                "Secret entry '{SecretKey}' holds malformed data and is treated as missing.",
                key
            );

            return null;
        }
    }

    private static string ToHex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}
