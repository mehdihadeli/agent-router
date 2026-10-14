using AgentRouter.Core;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Hosting;

public sealed class GatewayWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ccg-workflow-" + Guid.NewGuid().ToString("N")
    );
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
        await _configuration.AddProviderAsync(
            "fast",
            "https://example.test/v1",
            "upstream-test-secret",
            ["small", "small", "large"]
        );
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
        await using var connection = await _services
            .GetRequiredService<SqliteConnectionFactory>()
            .OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_state WHERE key = 'config'";
        var persistedJson = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain("upstream-test-secret", persistedJson, StringComparison.Ordinal);
    }

    private const string SettingsFixture = """
        {
          "schemaVersion": 1,
          "server": { "port": 4601 },
          "providers": {
            "work": {
              "protocol": "OpenAi", "endpoint": "https://example.test/Case/v1",
              "models": ["owner/main", "cheap"],
              "credentials": [{ "id": "primary", "environment": "UNRESOLVED_TEST_KEY" }]
            }
          },
          "routing": { "defaultModel": "work/owner/main", "aliases": { "quick": "work/cheap" } }
        }
        """;

    private GatewaySettings LoadSettings()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, SettingsFixture);
        return GatewaySettingsLoader.Load(
            path,
            _directory,
            environmentOverride: new ConfigurationBuilder().Build()
        );
    }

    [Fact]
    public void RuntimeSettings_DoesNotReapplySavedContractFieldsOrPersistResolvedCredentials()
    {
        var settings = LoadSettings();
        settings.Server.TokenEnvironment = "SYNTHETIC_GATEWAY_TOKEN";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SYNTHETIC_GATEWAY_TOKEN"] = "gateway-secret",
                    ["UNRESOLVED_TEST_KEY"] = "provider-secret",
                    ["Server:AuthToken"] = "unrelated-saved-token",
                    ["Server:ProxyPort"] = "4999",
                    ["Providers:work:BaseUrl"] = "https://unrelated.test/v1",
                    ["Providers:work:Models:0"] = "unrelated-model",
                }
            )
            .Build();
        var document = GatewayRuntimeConfiguration.ResolveSettings(settings, configuration);
        var runtime = GatewayRuntimeConfiguration.Project(
            document,
            configuration,
            applyOverrides: false
        );
        Assert.Equal("gateway-secret", document.Server.AuthToken);
        Assert.Equal("http://127.0.0.1:4601", runtime.GatewayBaseAddress);
        var provider = Assert.Single(runtime.Providers);
        Assert.Equal("https://example.test/Case/v1", provider.BaseAddress);
        Assert.Equal("provider-secret", provider.ApiKey);
        Assert.Equal(new[] { "owner/main", "cheap" }, provider.Models);
        Assert.Equal("work/owner/main", runtime.RouteModel.DefaultModel);
        Assert.Equal(
            "UNRESOLVED_TEST_KEY",
            Assert.Single(settings.Providers["work"].Credentials).Environment
        );
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.db")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AR__")]
    public void Settings_FileDotEnvEnvironmentAndArguments_ComposeWithoutPersistence(string prefix)
    {
        var settings = LoadSettings();
        var path = Path.Combine(_directory, "settings.json");
        var envPath = Path.Combine(_directory, ".env");
        File.WriteAllText(
            envPath,
            $"{prefix}Server__Port=4602\n{prefix}Routing__DefaultModel=work/cheap\n"
        );
        var dotenv = new ConfigurationBuilder().AddDotEnvFile(envPath, false).Build();
        Assert.Equal(4601, settings.Server.Port);
        Assert.Equal("work/owner/main", settings.Routing.DefaultModel);
        var fromDotenv = GatewaySettingsLoader.Load(path, _directory, environmentOverride: dotenv);
        Assert.Equal(4602, fromDotenv.Server.Port);
        Assert.Equal("work/cheap", fromDotenv.Routing.DefaultModel);
        var variables = new ConfigurationBuilder()
            .AddConfiguration(dotenv)
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [prefix.Replace("__", ":") + "Server:Port"] = "4603",
                }
            )
            .Build();
        Assert.Equal(
            4603,
            GatewaySettingsLoader.Load(path, _directory, environmentOverride: variables).Server.Port
        );
        var explicitSettings = GatewaySettingsLoader.Load(
            path,
            _directory,
            new Dictionary<string, string?>
            {
                ["Server:Port"] = "4604",
                ["Routing:DefaultModel"] = "work/owner/main",
            },
            variables
        );
        Assert.Equal(4604, explicitSettings.Server.Port);
        Assert.Equal("work/owner/main", explicitSettings.Routing.DefaultModel);
        Assert.Equal(SettingsFixture, File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public void Settings_DirectPathsOverrideLegacyAliasesAndIgnoreUnrelatedEnvironment()
    {
        LoadSettings();
        var variables = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AR:Server:Port"] = "4602",
                    ["Server:Port"] = "4603",
                    ["Providers:work:Endpoint"] = "https://overridden.test/v1",
                    ["Execution:MaxAttempts"] = "2",
                    ["PATH"] = "synthetic-system-path",
                    ["OpenAI:Endpoint"] = "https://unrelated-live-test.test/v1",
                }
            )
            .Build();
        var settings = GatewaySettingsLoader.Load(
            Path.Combine(_directory, "settings.json"),
            _directory,
            environmentOverride: variables
        );
        Assert.Equal(4603, settings.Server.Port);
        Assert.Equal("https://overridden.test/v1", settings.Providers["work"].Endpoint);
        Assert.Equal(2, settings.Execution.MaxAttempts);
        variables["Server:UnknownProperty"] = "invalid";
        Assert.Throws<InvalidOperationException>(() =>
            GatewaySettingsLoader.Load(
                Path.Combine(_directory, "settings.json"),
                _directory,
                environmentOverride: variables
            )
        );
        Assert.Equal(SettingsFixture, File.ReadAllText(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public void Settings_RootDiscovery_UsesNearestMarkerIncludingWorktreeFiles()
    {
        var nestedRoot = Path.Combine(_directory, "worktree");
        var child = Path.Combine(nestedRoot, "src", "feature");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(_directory, "global.json"), "{}");
        Assert.Equal(_directory, GatewaySettingsLoader.FindProjectRoot(child));
        File.WriteAllText(Path.Combine(nestedRoot, ".git"), "gitdir: unused-test-path");
        Assert.Equal(nestedRoot, GatewaySettingsLoader.FindProjectRoot(child));
        File.Delete(Path.Combine(nestedRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(nestedRoot, ".git"));
        Assert.Equal(nestedRoot, GatewaySettingsLoader.FindProjectRoot(child));
        File.Delete(Path.Combine(_directory, "global.json"));
        Directory.Delete(Path.Combine(nestedRoot, ".git"));
        Assert.Equal(child, GatewaySettingsLoader.FindProjectRoot(child));
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("server:port", "0")]
    [InlineData("server:host", "0.0.0.0")]
    [InlineData("execution:maxAttempts", "9")]
    [InlineData("execution:deadlineSeconds", "601")]
    [InlineData("execution:credentialCooldownSeconds", "-1")]
    [InlineData("routing:defaultModel", "missing/owner/main")]
    [InlineData("providers:work:enabled", "false")]
    [InlineData("providers:work:protocol", "Unknown")]
    [InlineData("providers:work:endpoint", "http://remote.test/v1")]
    [InlineData("providers:work:endpoint", "https://private:password@example.test/v1")]
    [InlineData("providers:work:endpoint", "https://example.test/v1?key=private")]
    [InlineData("providers:work:credentials:0:secret", "secret-id")]
    [InlineData("providers:work:apiKey", "plaintext-not-supported")]
    [InlineData("routing:rewritess", "typo")]
    public void Settings_InvalidOverrides_RejectWithoutChangingSavedInput(string key, string value)
    {
        LoadSettings();
        var path = Path.Combine(_directory, "settings.json");
        Assert.Throws<InvalidOperationException>(() =>
            GatewaySettingsLoader.Load(
                path,
                _directory,
                new Dictionary<string, string?> { [key] = value },
                new ConfigurationBuilder().Build()
            )
        );
        Assert.Equal(SettingsFixture, File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("task")]
    [InlineData("subagent")]
    [InlineData("workflow")]
    [InlineData("text")]
    [InlineData("requestedModel")]
    public void Settings_Routing_AllSelectorsUseFirstMatchingRule(string selector)
    {
        var settings = LoadSettings();
        var rule = new SettingsRoute
        {
            Id = "selected",
            Model = "work/cheap",
            Rewrite = new() { MaxOutputTokens = 64, Temperature = 0.2f },
        };
        var context = selector switch
        {
            "agent" => new SettingsRouteContext(Agent: rule.Agent = "review"),
            "task" => new SettingsRouteContext(Task: rule.Task = "summarize"),
            "subagent" => new SettingsRouteContext(Subagent: rule.Subagent = "worker"),
            "workflow" => new SettingsRouteContext(Workflow: rule.Workflow = "audit"),
            "text" => new SettingsRouteContext(Text: rule.ContainsText = "review code"),
            _ => new SettingsRouteContext(RequestedModel: rule.RequestedModel = "virtual-model"),
        };
        settings.Routing.Rules.Add(
            new SettingsRoute
            {
                Id = "disabled",
                Model = "work/owner/main",
                Enabled = false,
            }
        );
        settings.Routing.Rules.Add(rule);
        settings.Routing.Rules.Add(new SettingsRoute { Id = "later", Model = "work/owner/main" });
        var decision = SettingsRouteEvaluator.Preview(settings, context);
        Assert.Equal("work", decision.Provider);
        Assert.Equal("cheap", decision.Model);
        Assert.Equal("selected", decision.Rule);
        Assert.Equal("rule", decision.Reason);
        Assert.Equal(64, decision.MaxOutputTokens);
        Assert.Equal(0.2f, decision.Temperature);
    }

    [Fact]
    public void Settings_Preview_PreservesInputAndNativeModelsWithoutResolvingSecrets()
    {
        var settings = LoadSettings();
        settings.Routing.Rules.Add(
            new SettingsRoute
            {
                Id = "task",
                Task = "review",
                Model = "work/cheap",
                FallbackModels = ["work/owner/main"],
            }
        );
        settings.Execution.FallbackModels.Add("work/cheap");
        settings.Agents["review"] = new SettingsAgent { Kind = "codex", Model = "work/cheap" };
        var before = System.Text.Json.JsonSerializer.Serialize(settings);
        var explicitDecision = SettingsRouteEvaluator.Preview(
            settings,
            new("WORK/owner/main", Task: "review")
        );
        Assert.Equal("owner/main", explicitDecision.Model);
        Assert.Equal("explicit", explicitDecision.Reason);
        Assert.Null(explicitDecision.Rule);
        Assert.Null(explicitDecision.MaxOutputTokens);
        Assert.Equal("cheap", SettingsRouteEvaluator.Preview(settings, new("quick")).Model);
        Assert.Equal(
            "agent",
            SettingsRouteEvaluator.Preview(settings, new(Agent: "review")).Reason
        );
        Assert.Equal("default", SettingsRouteEvaluator.Preview(settings, new()).Reason);
        var matched = SettingsRouteEvaluator.Preview(settings, new(Task: "review"));
        Assert.Equal(new[] { "work/cheap", "work/owner/main" }, matched.Candidates);
        Assert.Equal(3, matched.MaxAttempts);
        Assert.Equal(60, matched.DeadlineSeconds);
        Assert.Throws<InvalidOperationException>(() =>
            SettingsRouteEvaluator.Preview(settings, new("work/missing"))
        );
        Assert.Throws<InvalidOperationException>(() =>
            SettingsRouteEvaluator.Preview(settings, new("unknown"))
        );
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(settings));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("task")]
    [InlineData("subagent")]
    [InlineData("workflow")]
    [InlineData("text")]
    public void Settings_Routing_RequiresEveryConditionAndDoesNotRewriteNonmatches(string mismatch)
    {
        var settings = LoadSettings();
        settings.Routing.Rules.Add(
            new SettingsRoute
            {
                Id = "combined",
                Model = "work/cheap",
                Agent = "review",
                Task = "audit",
                Subagent = "worker",
                Workflow = "pipeline",
                ContainsText = "review",
                Rewrite = new() { MaxOutputTokens = 32, Temperature = 0.1f },
            }
        );
        var matching = new SettingsRouteContext(
            Agent: "review",
            Task: "audit",
            Subagent: "worker",
            Workflow: "pipeline",
            Text: "Please REVIEW this"
        );
        Assert.Equal("combined", SettingsRouteEvaluator.Preview(settings, matching).Rule);
        var context = mismatch switch
        {
            "agent" => matching with { Agent = "other" },
            "task" => matching with { Task = null },
            "subagent" => matching with { Subagent = "other" },
            "workflow" => matching with { Workflow = null },
            _ => matching with { Text = "unrelated" },
        };
        var decision = SettingsRouteEvaluator.Preview(settings, context);
        Assert.Equal("default", decision.Reason);
        Assert.Equal("owner/main", decision.Model);
        Assert.Null(decision.Rule);
        Assert.Null(decision.MaxOutputTokens);
        Assert.Null(decision.Temperature);
    }

    [Theory]
    [InlineData("attempts", 0, false)]
    [InlineData("attempts", 1, true)]
    [InlineData("attempts", 8, true)]
    [InlineData("attempts", 9, false)]
    [InlineData("deadline", 0, false)]
    [InlineData("deadline", 1, true)]
    [InlineData("deadline", 600, true)]
    [InlineData("deadline", 601, false)]
    [InlineData("cooldown", -1, false)]
    [InlineData("cooldown", 0, true)]
    [InlineData("cooldown", 3600, true)]
    [InlineData("cooldown", 3601, false)]
    [InlineData("tokens", 0, false)]
    [InlineData("tokens", 1, true)]
    [InlineData("tokens", 131072, true)]
    [InlineData("tokens", 131073, false)]
    public void Settings_NumericBounds_AcceptEndpointsAndRejectOutside(
        string limit,
        int value,
        bool valid
    )
    {
        var settings = LoadSettings();
        var rule = new SettingsRoute { Id = "selected", Model = "work/cheap" };
        settings.Routing.Rules.Add(rule);
        switch (limit)
        {
            case "attempts":
                settings.Execution.MaxAttempts = value;
                break;
            case "deadline":
                settings.Execution.DeadlineSeconds = value;
                break;
            case "cooldown":
                settings.Execution.CredentialCooldownSeconds = value;
                break;
            case "tokens":
                rule.Rewrite.MaxOutputTokens = value;
                break;
        }
        if (!valid)
            Assert.Throws<InvalidOperationException>(() =>
                SettingsRouteEvaluator.Preview(settings, new())
            );
        else
        {
            var decision = SettingsRouteEvaluator.Preview(settings, new());
            Assert.Equal("cheap", decision.Model);
            Assert.Equal("selected", decision.Rule);
            if (limit == "attempts")
                Assert.Equal(value, decision.MaxAttempts);
            if (limit == "deadline")
                Assert.Equal(value, decision.DeadlineSeconds);
            if (limit == "tokens")
                Assert.Equal(value, decision.MaxOutputTokens);
        }
    }

    [Fact]
    public async Task Projection_JsonPathsOverrideSavedValuesWithoutPersistence()
    {
        await _configuration.AddProviderAsync(
            "work",
            "https://saved.test/v1",
            "saved-key",
            ["saved", "owner/override"]
        );
        await _configuration.SetRouteAsync("work/saved");
        var document = await _configuration.LoadAsync();
        var overrides = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Server:ProxyPort"] = "4702",
                    ["Providers:work:BaseUrl"] = "https://environment.test/v1",
                    ["Providers:work:ApiKey"] = "synthetic-environment-key",
                    ["Routing:Default:Model"] = "owner/override",
                    ["EnableTelemetry"] = "false",
                    ["PATH"] = "unrelated",
                }
            )
            .Build();
        var options = GatewayRuntimeConfiguration.Project(document, overrides);
        Assert.Equal("http://127.0.0.1:4702", options.GatewayBaseAddress);
        Assert.Equal("work/owner/override", options.RouteModel.DefaultModel);
        Assert.False(options.EnableTelemetry);
        var provider = Assert.Single(options.Providers);
        Assert.Equal("https://environment.test/v1", provider.BaseAddress.TrimEnd('/'));
        Assert.Equal("synthetic-environment-key", provider.ApiKey);
        Assert.Equal("https://saved.test/v1", document.Providers["work"].BaseUrl);
        Assert.Equal("saved-key", document.Providers["work"].ApiKey);
        Assert.Equal("saved", document.Routing.Default.Model);
        var saved = await _configuration.LoadAsync();
        Assert.Equal("https://saved.test/v1", saved.Providers["work"].BaseUrl);
        Assert.Equal("saved-key", saved.Providers["work"].ApiKey);
        Assert.Equal("saved", saved.Routing.Default.Model);
        overrides["Server:ProxyPort"] = "0";
        Assert.Throws<InvalidOperationException>(() =>
            GatewayRuntimeConfiguration.Project(document, overrides)
        );
    }

    [Fact]
    public async Task Projection_SeparatesProvidersWithSameProtocolAndModel()
    {
        await _configuration.AddProviderAsync(
            "first",
            "https://first.test/v1",
            "first-key",
            ["shared"]
        );
        await _configuration.AddProviderAsync(
            "second",
            "https://second.test/v1",
            "second-key",
            ["shared"]
        );
        await _configuration.SetRouteAsync("second/shared");
        var document = await _configuration.LoadAsync();
        var legacy = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Providers:0:ProviderType"] = "Azure",
                    ["Providers:0:Models:0"] = "stale-model",
                }
            )
            .Build();
        var options = GatewayRuntimeConfiguration.Project(document, legacy);

        Assert.Equal(2, options.Providers.Count);
        Assert.Equal("second/shared", options.RouteModel.DefaultModel);
        Assert.Equal(
            "https://second.test/v1",
            RouteModelFactory.Resolve(options, "second/shared").Provider.BaseAddress.TrimEnd('/')
        );
        Assert.Equal(
            "second/shared",
            RouteModelFactory.ResolveServiceKey(options, "SECOND/shared")
        );
        Assert.Equal("shared", RouteModelFactory.GetProviderModelId(options, "second/shared"));
    }

    [Fact]
    public async Task AnthropicProvider_PersistsProtocolAndProjectsNativeTransport()
    {
        await _configuration.AddProviderAsync(
            "claude",
            "https://anthropic.test",
            "anthropic-secret",
            ["native"],
            compatibility: ProviderCompatibility.Anthropic
        );
        await _configuration.SetRouteAsync("claude/native");
        var document = await _configuration.LoadAsync();
        Assert.Equal(ProviderCompatibility.Anthropic, document.Providers["claude"].Compatibility);
        var options = GatewayRuntimeConfiguration.Project(
            document,
            new ConfigurationBuilder().Build()
        );
        Assert.Equal(ProviderType.Anthropic, Assert.Single(options.Providers).ProviderType);
        Assert.Equal("claude/native", options.RouteModel.DefaultModel);
        Assert.Equal("anthropic-secret", options.Providers[0].ApiKey);
    }

    [Fact]
    public void LayeredConfiguration_EnvironmentOverridesDotEnvAndJson()
    {
        var jsonPath = Path.Combine(_directory, "appsettings.json");
        var envPath = Path.Combine(_directory, ".env");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            jsonPath,
            """{"LiveProviders":{"OpenAI":{"Endpoint":"https://json.test/v1","Model":"json-model"}}}"""
        );
        File.WriteAllText(
            envPath,
            "LiveProviders__OpenAI__Endpoint=https://dotenv.test/v1\nLiveProviders__OpenAI__Model=dotenv-model\n"
        );
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(jsonPath)
            .AddEnvironmentVariables()
            .AddDotEnvFile(envPath, optional: false)
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LiveProviders:OpenAI:Model"] = "override-model",
                }
            )
            .Build();
        Assert.Equal("https://dotenv.test/v1", configuration["LiveProviders:OpenAI:Endpoint"]);
        Assert.Equal("override-model", configuration["LiveProviders:OpenAI:Model"]);
        var sources = new ConfigurationBuilder()
            .AddJsonFile(jsonPath)
            .AddEnvironmentVariables()
            .AddDotEnvFile(envPath, false);
        Assert.IsType<Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource>(
            sources.Sources[^1]
        );
    }

    [Fact]
    public async Task RequestRouting_PrefersRecognizedModelThenTierThenDefault()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "test-key",
            ["main", "cheap", "owner/model"]
        );
        await _configuration.SetRouteAsync("provider/cheap", RoutingTier.Haiku);
        var options = GatewayRuntimeConfiguration.Project(
            await _configuration.LoadAsync(),
            new ConfigurationBuilder().Build()
        );

        Assert.Equal(
            "owner/model",
            RouteModelFactory.ResolveRequestModel(options, "provider/owner/model").Model
        );
        Assert.Equal(
            "cheap",
            RouteModelFactory.ResolveRequestModel(options, "claude-haiku-4-5").Model
        );
        Assert.Equal(
            "main",
            RouteModelFactory.ResolveRequestModel(options, "provider/missing").Model
        );
        Assert.Equal(
            "main",
            RouteModelFactory.ResolveRequestModel(options, "unknown/requested").Model
        );
        Assert.Equal("main", RouteModelFactory.ResolveRequestModel(options, null).Model);
    }

    [Fact]
    public async Task Launcher_PreservesArgumentsAndOnlySetsChildEnvironment()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["owner/model"]
        );
        await _configuration.AddProfileAsync("Review", "provider/owner/model", "agent.exe");
        var document = await _configuration.LoadAsync();
        var parentToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var startInfo = GatewayAgentLauncher.CreateStartInfo(
            document,
            "review",
            new Uri("http://127.0.0.1:49300"),
            ["--prompt", "hello; do not execute shell"]
        );

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

    [Fact]
    public async Task Launcher_AppliesProfileEnvironmentAndWorkingDirectoryToChildOnly()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["owner/model"]
        );
        await _configuration.AddProfileAsync("Review", "provider/owner/model", "agent.exe");
        var document = await _configuration.LoadAsync();
        var profile = Assert.Single(document.Profiles);
        profile.WorkingDirectory = Path.GetFullPath("review-workspace");
        profile.Environment["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = "2048";
        var info = GatewayAgentLauncher.CreateStartInfo(
            document,
            "Review",
            new Uri("http://127.0.0.1:49300")
        );

        Assert.Equal(profile.WorkingDirectory, info.WorkingDirectory);
        Assert.Equal("2048", info.Environment["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]);
        Assert.Null(Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_OUTPUT_TOKENS"));
    }

    [Fact]
    public void Launcher_RejectsProfileEnvironmentOverridesForGatewayCredentials()
    {
        var profile = new AgentProfile
        {
            Name = "Review",
            Environment = new Dictionary<string, string> { ["ANTHROPIC_AUTH_TOKEN"] = "override" },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GatewayAgentLauncher.ValidateProfile(profile)
        );

        Assert.Contains("controlled by AgentRouter", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://remote.example")]
    [InlineData("http://127.0.0.1/path")]
    [InlineData("http://user:password@localhost")]
    [InlineData("http://localhost/?token=secret")]
    public async Task Launcher_RejectsUnsafeGatewayDestinations(string url)
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "test-key",
            ["model"]
        );
        await _configuration.AddProfileAsync("Review", "provider/model");
        var document = await _configuration.LoadAsync();
        Assert.Throws<ArgumentException>(() =>
            GatewayAgentLauncher.CreateStartInfo(document, "Review", new Uri(url))
        );
    }

    [Fact]
    public async Task Launcher_CodexUsesChildOnlyProviderOverridesAndKeepsSecretsOutOfArguments()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["owner/model"]
        );
        await _configuration.AddProfileAsync(
            "Codex",
            "provider/owner/model",
            "native-codex.exe",
            "codex"
        );
        var document = await _configuration.LoadAsync();
        var parentToken = Environment.GetEnvironmentVariable("AR_AGENT_TOKEN");
        var info = GatewayAgentLauncher.CreateStartInfo(
            document,
            "Codex",
            new Uri("http://127.0.0.1:49300"),
            ["exec", "hello; literal"]
        );
        Assert.False(info.UseShellExecute);
        Assert.Equal("native-codex.exe", info.FileName);
        Assert.Contains("model=\"provider/owner/model\"", info.ArgumentList);
        Assert.Contains(
            "model_providers.agentrouter.base_url=\"http://127.0.0.1:49300/v1\"",
            info.ArgumentList
        );
        Assert.Contains(
            "model_providers.agentrouter.env_key=\"AR_AGENT_TOKEN\"",
            info.ArgumentList
        );
        Assert.Equal(new[] { "exec", "hello; literal" }, info.ArgumentList.TakeLast(2));
        Assert.DoesNotContain(
            info.ArgumentList,
            argument => argument.Contains(document.Server.AuthToken!, StringComparison.Ordinal)
        );
        Assert.Equal(document.Server.AuthToken, info.Environment["AR_AGENT_TOKEN"]);
        Assert.False(info.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.Equal(parentToken, Environment.GetEnvironmentVariable("AR_AGENT_TOKEN"));
    }

    [Fact]
    public async Task Launcher_OpenCodeUsesOpenAiCompatibleChildEnvironment()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["owner/model"]
        );
        await _configuration.AddProfileAsync(
            "OpenCode",
            "provider/owner/model",
            "opencode.exe",
            "opencode"
        );
        var document = await _configuration.LoadAsync();
        var info = GatewayAgentLauncher.CreateStartInfo(
            document,
            "OpenCode",
            new Uri("http://127.0.0.1:49300")
        );

        Assert.Equal("opencode.exe", info.FileName);
        Assert.Equal("http://127.0.0.1:49300/v1", info.Environment["OPENAI_BASE_URL"]);
        Assert.Equal(document.Server.AuthToken, info.Environment["OPENAI_API_KEY"]);
        Assert.Equal("provider/owner/model", info.Environment["OPENCODE_MODEL"]);
        Assert.Equal("OpenCode", info.Environment["X_AR_AGENT"]);
        Assert.DoesNotContain(info.ArgumentList, argument => argument.Contains("upstream-key"));
    }

    [Fact]
    public async Task Launcher_ClaudeDesignRejectsCliLaunch()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["model"]
        );
        await _configuration.AddProfileAsync(
            "Design",
            "provider/model",
            "claude-design.exe",
            "claude-design"
        );
        var document = await _configuration.LoadAsync();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GatewayAgentLauncher.CreateStartInfo(
                document,
                "Design",
                new Uri("http://127.0.0.1:49300")
            )
        );

        Assert.Contains("app-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task Launcher_ExplicitAgentTelemetryUsesChildSettingsAndRedactsContent(string kind)
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "upstream-key",
            ["model"]
        );
        await _configuration.AddProfileAsync(
            "Observed",
            "provider/model",
            "native-agent.exe",
            kind,
            new Uri("http://127.0.0.1:4318")
        );
        var document = await _configuration.LoadAsync();
        var parentEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var info = GatewayAgentLauncher.CreateStartInfo(
            document,
            "Observed",
            new Uri("http://127.0.0.1:49300")
        );
        if (kind == "claude")
        {
            Assert.Equal("1", info.Environment["CLAUDE_CODE_ENABLE_TELEMETRY"]);
            Assert.Equal("1", info.Environment["CLAUDE_CODE_ENHANCED_TELEMETRY_BETA"]);
            Assert.Equal("http://127.0.0.1:4318", info.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            Assert.Equal("0", info.Environment["OTEL_LOG_TOOL_CONTENT"]);
            Assert.Equal("0", info.Environment["OTEL_LOG_RAW_API_BODIES"]);
        }
        else
        {
            Assert.Contains("otel.log_user_prompt=false", info.ArgumentList);
            Assert.Contains(
                info.ArgumentList,
                argument =>
                    argument.StartsWith("otel.trace_exporter=", StringComparison.Ordinal)
                    && argument.Contains(
                        "http://127.0.0.1:4318/v1/traces",
                        StringComparison.Ordinal
                    )
            );
        }
        Assert.Equal(
            parentEndpoint,
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
        );
    }

    [Fact]
    public async Task InvalidMutation_DoesNotOverwriteSavedConfiguration()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "original-key",
            ["model"]
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _configuration.AddProviderAsync(
                "provider",
                "https://changed.test/v1",
                "replacement-key",
                ["other"]
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _configuration.SetRouteAsync("provider/missing")
        );
        var document = await _configuration.LoadAsync();
        Assert.Equal("original-key", document.Providers["provider"].ApiKey);
        Assert.Equal("https://example.test/v1", document.Providers["provider"].BaseUrl);
        Assert.False(document.Routing.Default.Enabled);
    }

    [Fact]
    public async Task Projection_RejectsUnsupportedProtocolAndDisabledModel()
    {
        await _configuration.AddProviderAsync(
            "provider",
            "https://example.test/v1",
            "test-key",
            ["model"]
        );
        var document = await _configuration.LoadAsync();
        document.Providers["provider"].AuthType = ProviderAuthType.OAuth;
        Assert.Throws<NotSupportedException>(() =>
            GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build())
        );
        document.Providers["provider"].AuthType = ProviderAuthType.ApiKey;
        document.Providers["provider"].DisabledModels.Add("model");
        Assert.Throws<InvalidOperationException>(() =>
            GatewayRuntimeConfiguration.Project(document, new ConfigurationBuilder().Build())
        );
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
