namespace AgentRouter.Core.Contracts.Stats;

/// <summary>Sanitized request health and usage metrics for a bounded time window.</summary>
public sealed record GatewayObservabilitySummary(
    DateTimeOffset From,
    DateTimeOffset To,
    int Requests,
    int SuccessfulRequests,
    int FailedRequests,
    double SuccessRate,
    double AverageLatencyMs,
    double P95LatencyMs,
    long InputTokens,
    long OutputTokens,
    decimal? CostUsd,
    string? TopProvider,
    string? TopModel,
    DateTimeOffset? LastRequestAt
);
