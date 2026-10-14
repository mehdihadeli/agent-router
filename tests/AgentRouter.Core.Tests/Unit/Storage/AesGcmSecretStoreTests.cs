using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Storage;

public class AesGcmSecretStoreTests
{
    private const string SecretKey = "provider.demo.apiKey";

    [Fact]
    public async Task Set_Then_Get_RoundTrips_Value()
    {
        using var harness = await StorageHarness.CreateAsync();

        await harness.SecretStore.SetAsync(SecretKey, "super-secret-value");

        var value = await harness.SecretStore.GetAsync(SecretKey);

        Assert.Equal("super-secret-value", value);
    }

    [Fact]
    public async Task Get_Returns_Null_For_Unknown_Key()
    {
        using var harness = await StorageHarness.CreateAsync();

        Assert.Null(await harness.SecretStore.GetAsync("provider.missing.apiKey"));
        Assert.False(await harness.SecretStore.ContainsAsync("provider.missing.apiKey"));
    }

    [Fact]
    public async Task Set_Ignores_Masked_Value()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.SecretStore.SetAsync(SecretKey, "original-value");

        await harness.SecretStore.SetAsync(SecretKey, AesGcmSecretStore.MaskToken + "alue");

        Assert.Equal("original-value", await harness.SecretStore.GetAsync(SecretKey));
    }

    [Fact]
    public async Task Set_Ignores_Empty_Value()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.SecretStore.SetAsync(SecretKey, "original-value");

        await harness.SecretStore.SetAsync(SecretKey, string.Empty);
        await harness.SecretStore.SetAsync(SecretKey, null);

        Assert.Equal("original-value", await harness.SecretStore.GetAsync(SecretKey));
    }

    [Fact]
    public async Task Remove_Deletes_Entry()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.SecretStore.SetAsync(SecretKey, "original-value");

        await harness.SecretStore.RemoveAsync(SecretKey);

        Assert.False(await harness.SecretStore.ContainsAsync(SecretKey));
        Assert.Null(await harness.SecretStore.GetAsync(SecretKey));
    }

    [Fact]
    public async Task Stored_Row_Never_Contains_Plaintext()
    {
        using var harness = await StorageHarness.CreateAsync();
        const string plaintext = "sk-live-1234567890abcdef";

        await harness.SecretStore.SetAsync(SecretKey, plaintext);

        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, nonce, ciphertext, tag FROM secret_entries;";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        var storedConcatenation =
            reader.GetString(0) + reader.GetString(1) + reader.GetString(2) + reader.GetString(3);

        Assert.DoesNotContain(plaintext, storedConcatenation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Same_Plaintext_Produces_Different_Ciphertext()
    {
        using var harness = await StorageHarness.CreateAsync();

        await harness.SecretStore.SetAsync(SecretKey, "repeated-value");
        var firstCiphertext = await ReadCiphertextAsync(harness, SecretKey);

        await harness.SecretStore.SetAsync(SecretKey, "repeated-value");
        var secondCiphertext = await ReadCiphertextAsync(harness, SecretKey);

        Assert.NotEqual(firstCiphertext, secondCiphertext);
    }

    [Fact]
    public async Task Get_Returns_Null_For_Undecryptable_Entry()
    {
        using var harness = await StorageHarness.CreateAsync();

        await using (var connection = await harness.ConnectionFactory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO secret_entries (key, nonce, ciphertext, tag, updated_at)
                VALUES ($key, '00', '00', '00', $updatedAt);
                """;
            command.Parameters.AddWithValue("$key", SecretKey);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        Assert.Null(await harness.SecretStore.GetAsync(SecretKey));
    }

    [Fact]
    public async Task Get_Returns_Null_When_Master_Key_Changes()
    {
        using var harness = await StorageHarness.CreateAsync();
        await harness.SecretStore.SetAsync(SecretKey, "original-value");

        var rotated = new AesGcmSecretStore(
            harness.ConnectionFactory,
            new MasterKey(new byte[MasterKey.KeySizeBytes]),
            NullLogger<AesGcmSecretStore>.Instance
        );

        Assert.Null(await rotated.GetAsync(SecretKey));
    }

    [Fact]
    public async Task GetAll_Returns_Every_Readable_Entry()
    {
        using var harness = await StorageHarness.CreateAsync();

        await harness.SecretStore.SetAsync("provider.one.apiKey", "one");
        await harness.SecretStore.SetAsync("provider.two.apiKey", "two");

        var all = await harness.SecretStore.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("one", all["provider.one.apiKey"]);
        Assert.Equal("two", all["provider.two.apiKey"]);
    }

    private static async Task<string> ReadCiphertextAsync(StorageHarness harness, string key)
    {
        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ciphertext FROM secret_entries WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
