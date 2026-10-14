using OpenAI;

namespace AgentRouter.Core.Providers.OpenAI;

public class OpenAIApiClientSettings
{
    public string? Endpoint { get; set; }
    public required string ApiKey { get; set; } = default!;
    public bool DisableTracing { get; set; } = false;
    internal Action<OpenAIClientOptions>? ConfigureClient { get; init; }
}
