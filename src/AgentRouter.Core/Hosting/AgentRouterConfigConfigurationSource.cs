using Microsoft.Extensions.Configuration;

namespace AgentRouter.Shared.Configuration;

public sealed class AgentRouterConfigConfigurationSource : IConfigurationSource
{
    public string? HomeOverridePath { get; set; }
    public bool CreateIfMissing { get; set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new AgentRouterConfigConfigurationProvider(HomeOverridePath, CreateIfMissing);
    }
}
