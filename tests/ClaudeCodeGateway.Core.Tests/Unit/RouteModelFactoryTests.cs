using ClaudeCodeGateway.Shared.AIProviders;
using ClaudeCodeGateway.Shared.Configuration;
using ClaudeCodeGateway.Shared.Contracts;
using ClaudeCodeGateway.Shared.Routes;
using Xunit;

namespace ClaudeCodeGateway.Tests;

public class RouteModelFactoryTests
{
    [Fact]
    public void GetProviderModelName_UsesGithubAlias_ForGitHubModelsProviderType()
    {
        var key = RouteModelFactory.GetProviderModelName(
            ProviderType.GitHubModels,
            "openai/gpt-4.1"
        );

        Assert.Equal("github/openai/gpt-4.1", key);
    }

    [Theory]
    [InlineData("github")]
    [InlineData("githubmodels")]
    [InlineData("github-models")]
    [InlineData("GitHubModels")]
    public void ParseProviderType_MapsGitHubAliases_ToGitHubModels(string providerName)
    {
        var providerType = RouteModelFactory.ParseProviderType(providerName);

        Assert.Equal(ProviderType.GitHubModels, providerType);
    }

    [Fact]
    public void ResolveServiceKey_NormalizesGitHubModelsPrefix_ToGithubAlias()
    {
        var options = new ClaudeGatewayOptions
        {
            Providers =
            [
                new ClaudeGatewayProvider
                {
                    ProviderType = ProviderType.GitHubModels,
                    BaseAddress = "https://models.github.ai/inference/",
                    ApiKey = "test",
                    Models = ["openai/gpt-4.1"],
                },
            ],
            RouteModel = new RouteModel
            {
                DefaultModel = "github/openai/gpt-4.1",
                ThinkingModel = "github/openai/gpt-4.1",
                SearchModel = "github/openai/gpt-4.1",
            },
        };

        var serviceKey = RouteModelFactory.ResolveServiceKey(
            options,
            "githubmodels/openai/gpt-4.1"
        );

        Assert.Equal("github/openai/gpt-4.1", serviceKey);
    }
}
