namespace AgentRouter.Contracts.Sessions;

/// <summary>
/// Truncated prompt/response capture for one request, stored separately from
/// <see cref="RequestLogEntry"/> so the hot request table stays small.
/// </summary>
public sealed class RequestBodies
{
    public long RequestId { get; set; }

    /// <summary>Human-readable serialization of the incoming prompt.</summary>
    public string? Prompt { get; set; }

    /// <summary>Sanitized preview of what was sent upstream.</summary>
    public string? RequestPreview { get; set; }

    /// <summary>Truncated response text captured as the stream completed.</summary>
    public string? Response { get; set; }

    public List<string> Warnings { get; set; } = new();
}
