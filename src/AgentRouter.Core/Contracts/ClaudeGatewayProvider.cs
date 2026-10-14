namespace AgentRouter.Core.Contracts;

public class ClaudeGatewayProvider
{
    public string? ProviderName { get; set; }

    public required ProviderType ProviderType { get; set; }

    public required string BaseAddress { get; set; } = default!;
    public required string ApiKey { get; set; } = default!;

    /// <summary>
    /// For Azure OpenAI, this is the deployment name.
    /// For OpenAI, it may be unused.
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Allow-list of models available for this provider.
    /// </summary>
    public required string[] Models { get; set; } = default!;
}
