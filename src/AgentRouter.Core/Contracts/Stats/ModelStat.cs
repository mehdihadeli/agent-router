namespace AgentRouter.Contracts.Stats;

using System.Text.Json.Serialization;

/// <summary>Per-model counters scoped to a session.</summary>
public sealed class ModelStat
{
    public string Model { get; set; } = string.Empty;

    public string? ProviderId { get; set; }

    public int Requests { get; set; }

    public int Errors { get; set; }

    public long TotalLatencyMs { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }

    /// <summary>Mean latency in milliseconds, or zero when no requests have been recorded.</summary>
    [JsonIgnore]
    public double AverageLatencyMs => Requests == 0 ? 0 : (double)TotalLatencyMs / Requests;

    /// <summary>Records one completed request.</summary>
    public void Record(int latencyMs, bool isError, DateTimeOffset at)
    {
        Requests++;
        TotalLatencyMs += latencyMs;
        LastActivityAt = at;

        if (isError)
        {
            Errors++;
        }
    }
}
