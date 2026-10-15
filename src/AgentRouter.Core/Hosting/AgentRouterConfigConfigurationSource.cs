namespace AgentRouter.Core.Hosting;

public sealed class AgentRouterConfigConfigurationSource : IConfigurationSource
{
    public string? HomeOverridePath { get; set; }
    public bool CreateIfMissing { get; set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new AgentRouterConfigConfigurationProvider(HomeOverridePath, CreateIfMissing);
    }
}
