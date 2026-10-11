using AgentRouter.Contracts.Stats;

namespace AgentRouter.Contracts.Sessions;

using System.Text.Json.Serialization;

/// <summary>
/// One recorded Claude Code session: either currently running, completed, or recovered after a
/// crash. Backs the Live Sessions and History panel screens.
/// </summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Running;

    /// <summary>Optional hint describing how the session was launched (provider, chain, mode).</summary>
    public string? LaunchHint { get; set; }

    /// <summary>Provider that served the session's primary model, when known.</summary>
    public string? ProviderId { get; set; }

    /// <summary>Primary model the session is bound to, when known.</summary>
    public string? Model { get; set; }

    public int RequestCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Rolling request log, newest last, capped by the writer.</summary>
    public List<RequestLogEntry> RequestLog { get; set; } = new();

    public List<ModelStat> Models { get; set; } = new();

    public List<ProviderStat> Providers { get; set; } = new();

    /// <summary>Session duration, or elapsed time so far when still running.</summary>
    [JsonIgnore]
    public TimeSpan Duration => (EndedAt ?? DateTimeOffset.UtcNow) - StartedAt;
}
