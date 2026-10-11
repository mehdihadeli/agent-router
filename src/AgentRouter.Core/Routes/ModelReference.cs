using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Contracts;

namespace AgentRouter.Shared.Routes;

public sealed record ModelReference(
    string Name,
    ProviderType ProviderType,
    string Model,
    ClaudeGatewayProvider Provider
);
