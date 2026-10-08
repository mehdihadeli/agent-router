using ClaudeCodeGateway.Shared.AIProviders;
using ClaudeCodeGateway.Shared.Contracts;

namespace ClaudeCodeGateway.Shared.Routes;

public sealed record ModelReference(
    string Name,
    ProviderType ProviderType,
    string Model,
    ClaudeGatewayProvider Provider
);
