using AgentRouter.Core.Contracts.Sessions;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Storage;

public sealed class GatewayRequestHistoryTests
{
    [Fact]
    public async Task History_DrainsOnStopAndPersistsReportedAndUnknownUsageWithoutInventingCost()
    {
        using var harness = await StorageHarness.CreateAsync();
        using var history = new GatewayRequestHistory(
            harness.ConnectionFactory,
            NullLogger<GatewayRequestHistory>.Instance
        );
        Assert.True(
            history.TryRecord(
                new(
                    "first",
                    DateTimeOffset.UtcNow,
                    "alias",
                    "provider",
                    "owner/model",
                    12.5,
                    "ok",
                    2,
                    1,
                    7,
                    3
                )
            )
        );
        Assert.True(
            history.TryRecord(
                new(
                    "second",
                    DateTimeOffset.UtcNow,
                    "alias",
                    "provider",
                    "owner/model",
                    20,
                    "error",
                    1,
                    1,
                    null,
                    null
                )
            )
        );
        await history.StartAsync(TestContext.Current.CancellationToken);
        await history.StopAsync(TestContext.Current.CancellationToken);
        var rows = await GatewayRequestHistory.ReadRecentAsync(
            harness.ConnectionFactory,
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Equal(2, rows.Count);
        Assert.Equal("second", rows[0].RequestId);
        Assert.Null(rows[0].InputTokens);
        Assert.Null(rows[0].OutputTokens);
        Assert.Null(rows[0].CostUsd);
        Assert.Equal(7, rows[1].InputTokens);
        Assert.Equal(3, rows[1].OutputTokens);
        Assert.Equal(2, rows[1].DispatchAttempts);
        Assert.Equal("owner/model", rows[1].Model);
        Assert.Null(rows[1].CostUsd);
        Assert.False(history.TryRecord(rows[0]));
    }

    [Fact]
    public async Task History_QueueIsBoundedAndRetentionDeletesExpiredSummaries()
    {
        using var harness = await StorageHarness.CreateAsync();
        using var bounded = new GatewayRequestHistory(
            harness.ConnectionFactory,
            NullLogger<GatewayRequestHistory>.Instance
        );
        var summary = new GatewayRequestSummary(
            "id",
            DateTimeOffset.UtcNow,
            null,
            "provider",
            "model",
            1,
            "ok",
            1,
            1,
            null,
            null
        );
        for (var index = 0; index < 1024; index++)
            Assert.True(bounded.TryRecord(summary));
        Assert.False(bounded.TryRecord(summary));
        using var history = new GatewayRequestHistory(
            harness.ConnectionFactory,
            NullLogger<GatewayRequestHistory>.Instance
        );
        history.TryRecord(
            summary with
            {
                RequestId = "expired",
                Timestamp = DateTimeOffset.UtcNow.AddDays(-31),
            }
        );
        history.TryRecord(summary with { RequestId = "current" });
        await history.StartAsync(TestContext.Current.CancellationToken);
        await history.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "current",
            Assert
                .Single(
                    await GatewayRequestHistory.ReadRecentAsync(
                        harness.ConnectionFactory,
                        cancellationToken: TestContext.Current.CancellationToken
                    )
                )
                .RequestId
        );
    }
}
