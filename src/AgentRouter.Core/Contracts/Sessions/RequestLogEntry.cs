using System.Text.Json.Serialization;

namespace AgentRouter.Core.Contracts.Sessions;

/// <summary>
/// A single proxied request. Rows are append-only and power Live Sessions and History.
/// </summary>
public sealed class RequestLogEntry
{
    /// <summary>Monotonic row id assigned by storage. Zero before it is persisted.</summary>
    public long Id { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Model id exactly as the client requested it (may carry a gateway prefix).</summary>
    public string? RequestedModel { get; set; }

    /// <summary>Provider that served the request.</summary>
    public string? ProviderId { get; set; }

    /// <summary>Model name actually sent upstream, after prefix stripping and aliasing.</summary>
    public string? ProviderModel { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int LatencyMs { get; set; }

    /// <summary>Outcome label, e.g. <c>ok</c>, <c>error</c>, <c>cancelled</c>.</summary>
    public string Status { get; set; } = "ok";

    public string? Error { get; set; }

    /// <summary>True when a matching <see cref="RequestBodies"/> row exists.</summary>
    public bool HasBodies { get; set; }

    /// <summary>Non-fatal conversion warnings surfaced for inspection.</summary>
    public List<string> Warnings { get; set; } = new();

    [JsonIgnore]
    public bool IsError => !string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
}
