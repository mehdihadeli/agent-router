namespace AgentRouter.Core.Contracts.Sessions;

public sealed record GatewayRequestSummary(
    string RequestId,
    DateTimeOffset Timestamp,
    string? RequestedModel,
    string? Provider,
    string? Model,
    double LatencyMs,
    string Outcome,
    int DispatchAttempts,
    int ProviderAttemptLimit,
    long? InputTokens,
    long? OutputTokens,
    decimal? CostUsd = null
);
