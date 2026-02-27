using Microsoft.Extensions.Configuration;

namespace Common.Configuration;

public sealed class ClaudeCodeGatewayConfigConfigurationSource : IConfigurationSource
{
    public string? HomeOverridePath { get; set; }
    public bool CreateIfMissing { get; set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new ClaudeCodeGatewayConfigConfigurationProvider(HomeOverridePath, CreateIfMissing);
    }
}
