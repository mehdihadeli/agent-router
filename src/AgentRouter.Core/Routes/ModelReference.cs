using AgentRouter.Core.Contracts;

namespace AgentRouter.Core.Routes;

public sealed record ModelReference(
    string Name,
    ProviderType ProviderType,
    string Model,
    ClaudeGatewayProvider Provider
);
