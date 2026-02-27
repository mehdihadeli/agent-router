using Common.AIProviders;

namespace Common.Routes;

public sealed record ModelReference(
    string Name,
    ProviderType ProviderType,
    string Model,
    ClaudeGatewayProvider Provider
);
