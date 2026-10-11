using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Xunit;

namespace AgentRouter.Tests;

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

    [Theory]
    [InlineData("openai/deepseek-flash", "deepseek-flash")]
    [InlineData("deepseek-flash", "deepseek-flash")]
    [InlineData("azure/gpt-4.1", "gpt-4.1")]
    [InlineData("unknown-provider/some-model", "unknown-provider/some-model")]
    [InlineData("", "")]
    public void GetProviderModelId_StripsPrefixOnlyForConfiguredProviders(
        string modelRoute,
        string expected
    )
    {
        var options = CreateOptions();

        var modelId = RouteModelFactory.GetProviderModelId(options, modelRoute);

        Assert.Equal(expected, modelId);
    }

    [Fact]
    public void GetProviderModelId_PreservesSlashInModelName_WhenPrefixIsAConfiguredProvider()
    {
        var options = CreateOptions();

        var modelId = RouteModelFactory.GetProviderModelId(
            options,
            "openai/meta-llama/Llama-3-70B"
        );

        Assert.Equal("meta-llama/Llama-3-70B", modelId);
    }

    private static ClaudeGatewayOptions CreateOptions() =>
        new()
        {
            Providers =
            [
                new ClaudeGatewayProvider
                {
                    ProviderType = ProviderType.OpenAI,
                    BaseAddress = "https://api.deepseek.com",
                    ApiKey = "test",
                    Models = ["deepseek-flash", "meta-llama/Llama-3-70B"],
                },
                new ClaudeGatewayProvider
                {
                    ProviderType = ProviderType.Azure,
                    BaseAddress = "https://example.openai.azure.com",
                    ApiKey = "test",
                    Models = ["gpt-4.1"],
                },
            ],
            RouteModel = new RouteModel
            {
                DefaultModel = "openai/deepseek-flash",
                ThinkingModel = "openai/deepseek-flash",
                SearchModel = "openai/deepseek-flash",
            },
        };
}
