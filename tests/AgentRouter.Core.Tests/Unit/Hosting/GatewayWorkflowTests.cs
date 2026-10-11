using AgentRouter;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Shared.Routes;
using AgentRouter.Storage;
using AgentRouter.Storage.Configuration;
using Microsoft.Extensions.Configuration;
using AgentRouter.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Tests;

public sealed class GatewayWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ccg-workflow-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly GatewayConfigurationService _configuration;

    public GatewayWorkflowTests()
    {
        var services = new ServiceCollection();
        services.AddGatewayStorage(options => options.Directory = _directory);
        services.AddSingleton<GatewayConfigurationService>();
        _services = services.BuildServiceProvider();
        _configuration = _services.GetRequiredService<GatewayConfigurationService>();
    }

    [Fact]
    public async Task Configuration_PersistsProvidersRoutesProfilesAndEncryptedKeys()
    {
        await _configuration.InitializeAsync(49300);
        await _configuration.AddProviderAsync("fast", "https://example.test/v1", "upstream-test-secret", ["small", "small", "large"]);
        await _configuration.SetRouteAsync("fast/large");
        await _configuration.SetRouteAsync("fast/small", RoutingTier.Haiku);
        await _configuration.AddProfileAsync("Review", "fast/large");

        var document = await _services.GetRequiredService<IConfigStore>().LoadAsync();
        Assert.Equal(49300, document.Server.ProxyPort);
        Assert.Equal("upstream-test-secret", document.Providers["fast"].ApiKey);
        Assert.Equal(new[] { "small", "large" }, document.Providers["fast"].Models);
        Assert.Equal("large", document.Routing.Default.Model);
        Assert.Equal("small", document.Routing.Haiku.Model);
        Assert.Equal("fast/large", Assert.Single(document.Profiles).Model);
        var masked = ConfigSecretSplitter.Mask(document);
        Assert.Null(masked.Server.AuthToken);
        Assert.NotEqual(document.Providers["fast"].ApiKey, masked.Providers["fast"].ApiKey);
        await using var connection = await _services.GetRequiredService<AgentRouter.Storage.Sqlite.SqliteConnectionFactory>().OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = 'config'";
        var persistedJson = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain("upstream-test-secret", persistedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projection_SeparatesProvidersWithSameProtocolAndModel()
    {
        await _configuration.AddProviderAsync("first", "https://first.test/v1", "first-key", ["shared"]);
        await _configuration.AddProviderAsync("second", "https://second.test/v1", "second-key", ["shared"]);
        await _configuration.SetRouteAsync("second/shared");
        var document = await _configuration.LoadAsync();
        var legacy = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:0:ProviderType"] = "Azure",
            ["Providers:0:Models:0"] = "stale-model",
        }).Build();
        var options = GatewayRuntimeConfiguration.Project(document, legacy);

        Assert.Equal(2, options.Providers.Count);
        Assert.Equal("second/shared", options.RouteModel.DefaultModel);
        Assert.Equal("https://second.test/v1", RouteModelFactory.Resolve(options, "second/shared").Provider.BaseAddress.TrimEnd('/'));
        Assert.Equal("second/shared", RouteModelFactory.ResolveServiceKey(options, "SECOND/shared"));
        Assert.Equal("shared", RouteModelFactory.GetProviderModelId(options, "second/shared"));
    }

    [Fact]
    public async Task AnthropicProvider_PersistsProtocolAndProjectsNativeTransport()
    {
        await _configuration.AddProviderAsync("claude", "https://anthropic.test", "anthropic-secret", ["native"], compatibility: ProviderCompatibility.Anthropic);
        await _configuration.SetRouteAsync("claude/native");
        var document = await _configuration.LoadAsync();
        Assert.Equal(ProviderCompatibility.Anthropic, document.Providers["claude"].Compatibility);
        var options = GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build());
        Assert.Equal(AgentRouter.Shared.AIProviders.ProviderType.Anthropic, Assert.Single(options.Providers).ProviderType);
        Assert.Equal("claude/native", options.RouteModel.DefaultModel);
        Assert.Equal("anthropic-secret", options.Providers[0].ApiKey);
    }

    [Fact]
    public void LayeredConfiguration_EnvironmentOverridesDotEnvAndJson()
    {
        var jsonPath = Path.Combine(_directory, "appsettings.json");
        var envPath = Path.Combine(_directory, ".env");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(jsonPath, """{"LiveProviders":{"OpenAI":{"Endpoint":"https://json.test/v1","Model":"json-model"}}}""");
        File.WriteAllText(envPath, "LiveProviders__OpenAI__Endpoint=https://dotenv.test/v1\nLiveProviders__OpenAI__Model=dotenv-model\n");
        var configuration = new ConfigurationBuilder().AddJsonFile(jsonPath).AddEnvironmentVariables()
            .AddDotEnvFile(envPath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LiveProviders:OpenAI:Model"] = "override-model" })
            .Build();
        Assert.Equal("https://dotenv.test/v1", configuration["LiveProviders:OpenAI:Endpoint"]);
        Assert.Equal("override-model", configuration["LiveProviders:OpenAI:Model"]);
        var sources = new ConfigurationBuilder().AddJsonFile(jsonPath).AddEnvironmentVariables().AddDotEnvFile(envPath, false);
        Assert.IsType<Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource>(sources.Sources[^1]);
    }

    [Fact]
    public async Task RequestRouting_PrefersRecognizedModelThenTierThenDefault()
    {
        await _configuration.AddProviderAsync("provider", "https://example.test/v1", "test-key", ["main", "cheap", "owner/model"]);
        await _configuration.SetRouteAsync("provider/cheap", RoutingTier.Haiku);
        var options = GatewayRuntimeConfiguration.Project(await _configuration.LoadAsync(), new ConfigurationBuilder().Build());

        Assert.Equal("owner/model", RouteModelFactory.ResolveRequestModel(options, "provider/owner/model").Model);
        Assert.Equal("cheap", RouteModelFactory.ResolveRequestModel(options, "claude-haiku-4-5").Model);
        Assert.Equal("main", RouteModelFactory.ResolveRequestModel(options, "provider/missing").Model);
        Assert.Equal("main", RouteModelFactory.ResolveRequestModel(options, "unknown/requested").Model);
        Assert.Equal("main", RouteModelFactory.ResolveRequestModel(options, null).Model);
    }

    [Fact]
    public async Task Launcher_PreservesArgumentsAndOnlySetsChildEnvironment()
    {
        await _configuration.AddProviderAsync("provider", "https://example.test/v1", "upstream-key", ["owner/model"]);
        await _configuration.AddProfileAsync("Review", "provider/owner/model", "agent.exe");
        var document = await _configuration.LoadAsync();
        var parentToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var startInfo = GatewayAgentLauncher.CreateStartInfo(document, "review", new Uri("http://127.0.0.1:49300"), ["--prompt", "hello; do not execute shell"]);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("agent.exe", startInfo.FileName);
        Assert.Equal(new[] { "--prompt", "hello; do not execute shell" }, startInfo.ArgumentList);
        Assert.Equal("http://127.0.0.1:49300", startInfo.Environment["ANTHROPIC_BASE_URL"]);
        Assert.Equal(document.Server.AuthToken, startInfo.Environment["ANTHROPIC_AUTH_TOKEN"]);
        Assert.NotEqual("upstream-key", startInfo.Environment["ANTHROPIC_AUTH_TOKEN"]);
        Assert.Equal("provider/owner/model", startInfo.Environment["ANTHROPIC_MODEL"]);
        Assert.False(startInfo.Environment.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.Equal(parentToken, Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"));
    }

    [Theory]
    [InlineData("https://remote.example")]
    [InlineData("http://127.0.0.1/path")]
    [InlineData("http://user:password@localhost")]
    [InlineData("http://localhost/?token=secret")]
    public async Task Launcher_RejectsUnsafeGatewayDestinations(string url)
    {
        await _configuration.AddProviderAsync("provider", "https://example.test/v1", "test-key", ["model"]);
        await _configuration.AddProfileAsync("Review", "provider/model");
        var document = await _configuration.LoadAsync();
        Assert.Throws<ArgumentException>(() => GatewayAgentLauncher.CreateStartInfo(document, "Review", new Uri(url)));
    }

    [Fact]
    public async Task InvalidMutation_DoesNotOverwriteSavedConfiguration()
    {
        await _configuration.AddProviderAsync("provider", "https://example.test/v1", "original-key", ["model"]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _configuration.AddProviderAsync("provider", "https://changed.test/v1", "replacement-key", ["other"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _configuration.SetRouteAsync("provider/missing"));
        var document = await _configuration.LoadAsync();
        Assert.Equal("original-key", document.Providers["provider"].ApiKey);
        Assert.Equal("https://example.test/v1", document.Providers["provider"].BaseUrl);
        Assert.False(document.Routing.Default.Enabled);
    }

    [Fact]
    public async Task Projection_RejectsUnsupportedProtocolAndDisabledModel()
    {
        await _configuration.AddProviderAsync("provider", "https://example.test/v1", "test-key", ["model"]);
        var document = await _configuration.LoadAsync();
        document.Providers["provider"].AuthType = ProviderAuthType.OAuth;
        Assert.Throws<NotSupportedException>(() => GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build()));
        document.Providers["provider"].AuthType = ProviderAuthType.ApiKey;
        document.Providers["provider"].DisabledModels.Add("model");
        Assert.Throws<InvalidOperationException>(() => GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build()));
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
