using ClaudeCodeGateway.Shared.AIProviders;
using Xunit;

namespace ClaudeCodeGateway.Providers.OpenAI.Tests;

public class ProviderRegistrationTests
{
    [Fact]
    public void Provider_registration_surface_is_available()
    {
        Assert.NotNull(typeof(ClientExtensions));
    }
}
