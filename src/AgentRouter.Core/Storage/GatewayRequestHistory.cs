using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading.Channels;
using AgentRouter.Core.Contracts.Sessions;
using AgentRouter.Core.Contracts.Stats;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Storage.Sqlite;

namespace AgentRouter.Core.Storage;

public sealed class GatewayRequestHistory(
    SqliteConnectionFactory connections,
    ILogger<GatewayRequestHistory> logger
) : BackgroundService
{
    private static readonly Meter Meter = new(GatewayInferenceClient.TelemetryName);
    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "agentrouter.history.dropped"
    );
    private readonly Channel<GatewayRequestSummary> _queue =
        Channel.CreateBounded<GatewayRequestSummary>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                AllowSynchronousContinuations = false,
            }
        );

    public bool TryRecord(GatewayRequestSummary summary)
    {
        if (_queue.Writer.TryWrite(summary))
            return true;
        Dropped.Add(1);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(connections.DatabasePath)!);
            await new GatewayDatabase(
                connections,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayDatabase>.Instance
            ).InitializeAsync(stoppingToken);
            await foreach (var summary in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var connection = await connections.OpenAsync(stoppingToken);
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        INSERT INTO inference_summaries (request_id, ts, data) VALUES ($request, $timestamp, $data);
                        DELETE FROM inference_summaries WHERE ts < $cutoff OR id NOT IN
                            (SELECT id FROM inference_summaries ORDER BY id DESC LIMIT 10000);
                        """;
                    command.Parameters.AddWithValue("$request", summary.RequestId);
                    command.Parameters.AddWithValue(
                        "$timestamp",
                        summary.Timestamp.ToUniversalTime().ToString("O")
                    );
                    command.Parameters.AddWithValue(
                        "$data",
                        JsonSerializer.Serialize(
                            summary,
                            AgentRouter.Core.Contracts.Serialization.GatewayJson.Options
                        )
                    );
                    command.Parameters.AddWithValue(
                        "$cutoff",
                        DateTimeOffset.UtcNow.AddDays(-30).ToString("O")
                    );
                    await command.ExecuteNonQueryAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Dropped.Add(1);
                    logger.LogWarning(
                        "Request summary could not be persisted ({ErrorType}).",
                        error.GetType().Name
                    );
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _queue.Writer.TryComplete();
            logger.LogWarning(
                "Request history is unavailable ({ErrorType}); inference remains enabled.",
                error.GetType().Name
            );
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        if (ExecuteTask is not null)
            await ExecuteTask.WaitAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public static async Task<IReadOnlyList<GatewayRequestSummary>> ReadRecentAsync(
        SqliteConnectionFactory connections,
        int limit = 100,
        CancellationToken cancellationToken = default
    ) =>
        await ReadSinceAsync(
            connections,
            DateTimeOffset.UtcNow.AddDays(-30),
            limit,
            cancellationToken
        );

    public static async Task<GatewayObservabilitySummary> SummarizeAsync(
        SqliteConnectionFactory connections,
        TimeSpan window,
        CancellationToken cancellationToken = default
    )
    {
        var to = DateTimeOffset.UtcNow;
        var from = to - window;
        var summaries = await ReadSinceAsync(connections, from, 10000, cancellationToken);
        var requests = summaries.Where(summary => summary.Timestamp >= from).ToArray();
        var successful = requests.Count(summary => summary.Outcome == "ok");
        var latencies = requests.Select(summary => summary.LatencyMs).Order().ToArray();
        var totalCost = requests
            .Where(summary => summary.CostUsd.HasValue)
            .Select(summary => summary.CostUsd!.Value)
            .ToArray();
        return new GatewayObservabilitySummary(
            from,
            to,
            requests.Length,
            successful,
            requests.Length - successful,
            requests.Length == 0 ? 0 : (double)successful / requests.Length,
            latencies.Length == 0 ? 0 : latencies.Average(),
            latencies.Length == 0 ? 0 : latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1],
            requests.Sum(summary => summary.InputTokens ?? 0),
            requests.Sum(summary => summary.OutputTokens ?? 0),
            totalCost.Length == 0 ? null : totalCost.Sum(),
            requests
                .Where(summary => !string.IsNullOrWhiteSpace(summary.Provider))
                .GroupBy(summary => summary.Provider!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault(),
            requests
                .Where(summary => !string.IsNullOrWhiteSpace(summary.Model))
                .GroupBy(summary => summary.Model!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault(),
            requests.Length == 0 ? null : requests.Max(summary => summary.Timestamp)
        );
    }

    private static async Task<IReadOnlyList<GatewayRequestSummary>> ReadSinceAsync(
        SqliteConnectionFactory connections,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken
    )
    {
        await new GatewayDatabase(
            connections,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayDatabase>.Instance
        ).InitializeAsync(cancellationToken);
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data FROM inference_summaries WHERE ts >= $cutoff ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10000));
        command.Parameters.AddWithValue("$cutoff", since.ToUniversalTime().ToString("O"));
        var summaries = new List<GatewayRequestSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            summaries.Add(
                JsonSerializer.Deserialize<GatewayRequestSummary>(
                    reader.GetString(0),
                    AgentRouter.Core.Contracts.Serialization.GatewayJson.Options
                )!
            );
        return summaries;
    }
}
