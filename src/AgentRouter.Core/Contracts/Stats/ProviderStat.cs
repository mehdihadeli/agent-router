using System.Text.Json.Serialization;

namespace AgentRouter.Core.Contracts.Stats;

/// <summary>
/// Durable per-provider counters. Mirrored into SQLite so the panel and the proxy share one view
/// even when they run as separate processes.
/// </summary>
public sealed class ProviderStat
{
    public string ProviderId { get; set; } = string.Empty;

    public int Requests { get; set; }

    public int Errors { get; set; }

    public long TotalLatencyMs { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Mean latency in milliseconds, or zero when no requests have been recorded.</summary>
    [JsonIgnore]
    public double AverageLatencyMs => Requests == 0 ? 0 : (double)TotalLatencyMs / Requests;

    /// <summary>Records one completed request.</summary>
    public void Record(int latencyMs, bool isError, DateTimeOffset at, string? error = null)
    {
        Requests++;
        TotalLatencyMs += latencyMs;
        LastActivityAt = at;

        if (isError)
        {
            Errors++;
            LastError = error;
        }
    }
}
