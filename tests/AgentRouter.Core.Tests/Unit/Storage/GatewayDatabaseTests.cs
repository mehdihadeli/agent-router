using AgentRouter.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentRouter.Tests;

public class GatewayDatabaseTests
{
    private static readonly string[] ExpectedTables =
    [
        "app_state",
        "logs",
        "provider_stats",
        "request_bodies",
        "requests",
        "secret_entries",
        "sessions",
    ];

    [Fact]
    public async Task Initialize_Creates_Every_Gateway_Table()
    {
        using var harness = await StorageHarness.CreateAsync();

        var tables = await ReadTableNamesAsync(harness);

        foreach (var expected in ExpectedTables)
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task Initialize_Sets_User_Version()
    {
        using var harness = await StorageHarness.CreateAsync();

        Assert.Equal(DatabaseSchema.CurrentVersion, await ReadUserVersionAsync(harness));
    }

    [Fact]
    public async Task Initialize_Is_Idempotent()
    {
        using var harness = await StorageHarness.CreateAsync();

        var database = new GatewayDatabase(
            harness.ConnectionFactory,
            NullLogger<GatewayDatabase>.Instance
        );
        await database.InitializeAsync();
        await database.InitializeAsync();

        Assert.Equal(DatabaseSchema.CurrentVersion, await ReadUserVersionAsync(harness));
    }

    [Fact]
    public async Task Initialize_Enables_Wal_Journal_Mode()
    {
        using var harness = await StorageHarness.CreateAsync();

        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        var mode = (string)(await command.ExecuteScalarAsync())!;

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task Initialize_Enables_Foreign_Keys_Per_Connection()
    {
        using var harness = await StorageHarness.CreateAsync();

        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private static async Task<List<string>> ReadTableNamesAsync(StorageHarness harness)
    {
        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> ReadUserVersionAsync(StorageHarness harness)
    {
        await using var connection = await harness.ConnectionFactory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
