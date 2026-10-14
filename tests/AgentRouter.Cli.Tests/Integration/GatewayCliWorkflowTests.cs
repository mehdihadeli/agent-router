using System.Diagnostics;
using System.Text.Json;
using AgentRouter.Cli;
using Xunit;

namespace AgentRouter.Cli.Tests.Integration;

public sealed class GatewayCliWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ccg-cli-" + Guid.NewGuid().ToString("N")
    );

    public GatewayCliWorkflowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Cli_ShortOptionsPreserveConfiguration()
    {
        Assert.Equal(0, (await RunAsync("init", "-p", "49302")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "provider",
                    "add",
                    "test",
                    "-u",
                    "https://example.test/v1",
                    "-m",
                    "small,large",
                    "-t",
                    "OpenAi",
                    "-e",
                    "OPENAI_API_KEY"
                )
            ).ExitCode
        );
        Assert.Equal(0, (await RunAsync("route", "set", "test/large", "-t", "Default")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync("profile", "add", "Review", "-m", "test/large", "-x", "claude")
            ).ExitCode
        );
        var shortShow = await RunAsync("config", "show");
        Assert.Equal(0, shortShow.ExitCode);
        using var json = JsonDocument.Parse(shortShow.Output);
        Assert.Equal(
            "large",
            json.RootElement.GetProperty("Routing")
                .GetProperty("Default")
                .GetProperty("Model")
                .GetString()
        );
        Assert.Equal(
            "Review",
            json.RootElement.GetProperty("Profiles")[0].GetProperty("Name").GetString()
        );
        Assert.Equal(
            49302,
            json.RootElement.GetProperty("Server").GetProperty("ProxyPort").GetInt32()
        );
        Assert.DoesNotContain("cli-upstream-secret", shortShow.Output, StringComparison.Ordinal);
        Assert.Contains(
            "test",
            (await RunAsync("provider", "list")).Output,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Review",
            (await RunAsync("profile", "list")).Output,
            StringComparison.Ordinal
        );
        var launch = await RunAsync(
            "profile",
            "launch",
            "Review",
            "--url",
            "http://127.0.0.1:1",
            "--",
            "--permission-mode",
            "plan"
        );
        Assert.NotEqual(0, launch.ExitCode);
        Assert.Contains(
            "Authenticated gateway is not available",
            launch.Output,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("service", "serve")]
    [InlineData("service", "start")]
    [InlineData("service", "stop")]
    [InlineData("service", "status")]
    [InlineData("profile", "launch")]
    [InlineData("profile", "add")]
    [InlineData("profile", "list")]
    [InlineData("profile", "edit")]
    [InlineData("profile", "remove")]
    [InlineData("route", "set")]
    [InlineData("service", "pause")]
    [InlineData("service", "resume")]
    [InlineData("service", "reload")]
    [InlineData("provider", "add")]
    [InlineData("provider", "list")]
    [InlineData("provider", "remove")]
    [InlineData("provider", "check")]
    [InlineData("config", "show")]
    [InlineData("config", "token")]
    [InlineData("config", "validate")]
    [InlineData("route", "preview")]
    public async Task Cli_GroupedHelpDoesNotExecuteCommands(string group, string command)
    {
        var result = await RunAsync(group, command, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("USAGE:", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "service.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public async Task Cli_InitProviderRouteAndProfile_PersistAcrossInvocations()
    {
        Assert.Equal(0, (await RunAsync("init", "--port", "49301")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "provider",
                    "add",
                    "test",
                    "--base-url",
                    "https://example.test/v1",
                    "--models",
                    "small,large"
                )
            ).ExitCode
        );
        Assert.Equal(0, (await RunAsync("route", "set", "test/large")).ExitCode);
        Assert.Equal(
            0,
            (await RunAsync("profile", "add", "Code Review", "--model", "test/large")).ExitCode
        );
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        Assert.DoesNotContain("cli-upstream-secret", show.Output, StringComparison.Ordinal);
        var token = await RunAsync("config", "token");
        Assert.Equal(0, token.ExitCode);
        Assert.StartsWith("sk_", token.Output.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain(token.Output.Trim(), show.Output, StringComparison.Ordinal);
        Assert.Equal(token.Output, (await RunAsync("config", "token")).Output);
        using var json = JsonDocument.Parse(show.Output);
        Assert.Equal(
            49301,
            json.RootElement.GetProperty("Server").GetProperty("ProxyPort").GetInt32()
        );
        Assert.Equal(
            "large",
            json.RootElement.GetProperty("Routing")
                .GetProperty("Default")
                .GetProperty("Model")
                .GetString()
        );
        Assert.Equal(
            "Code Review",
            json.RootElement.GetProperty("Profiles")[0].GetProperty("Name").GetString()
        );
        Assert.Contains(
            "Code Review",
            (await RunAsync("profile", "list")).Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Cli_ProfileEditPersistsExtendedSettingsAndRemoveDeletesProfile()
    {
        Assert.Equal(0, (await RunAsync("init", "--port", "49303")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "provider",
                    "add",
                    "test",
                    "--base-url",
                    "https://example.test/v1",
                    "--models",
                    "small,large"
                )
            ).ExitCode
        );
        Assert.Equal(0, (await RunAsync("route", "set", "test/large")).ExitCode);
        Assert.Equal(0, (await RunAsync("profile", "add", "Review")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "profile",
                    "edit",
                    "Review",
                    "--kind",
                    "opencode",
                    "--entry-mode",
                    "cli-and-app",
                    "--scope",
                    "system",
                    "--executable",
                    "opencode",
                    "--tier-sonnet",
                    "test/large",
                    "--telemetry-endpoint",
                    "http://127.0.0.1:4318",
                    "--working-directory",
                    Path.GetFullPath("review-workspace"),
                    "--env",
                    "CLAUDE_CODE_MAX_OUTPUT_TOKENS=2048"
                )
            ).ExitCode
        );

        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        using var json = JsonDocument.Parse(show.Output);
        var profile = json.RootElement.GetProperty("Profiles")[0];
        Assert.Equal("opencode", profile.GetProperty("Kind").GetString());
        Assert.Equal("cli-and-app", profile.GetProperty("EntryMode").GetString());
        Assert.Equal("system", profile.GetProperty("Scope").GetString());
        Assert.Equal("opencode", profile.GetProperty("Executable").GetString());
        Assert.Equal(
            "test/large",
            profile.GetProperty("TierModels").GetProperty("sonnet").GetString()
        );
        Assert.Equal(
            "http://127.0.0.1:4318/",
            profile.GetProperty("TelemetryEndpoint").GetString()
        );
        Assert.Equal(
            Path.GetFullPath("review-workspace"),
            profile.GetProperty("WorkingDirectory").GetString()
        );
        Assert.Equal(
            "2048",
            profile
                .GetProperty("Environment")
                .GetProperty("CLAUDE_CODE_MAX_OUTPUT_TOKENS")
                .GetString()
        );

        Assert.Equal(0, (await RunAsync("profile", "remove", "Review")).ExitCode);
        using var removed = JsonDocument.Parse((await RunAsync("config", "show")).Output);
        Assert.Empty(removed.RootElement.GetProperty("Profiles").EnumerateArray());
    }

    [Fact]
    public async Task Cli_NoArgumentsShowsHelpWithoutStartingServer()
    {
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("USAGE:", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "service.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public async Task Cli_ObservabilityShowsEmptyWindowWithoutInference()
    {
        Assert.Equal(0, (await RunAsync("init", "--port", "49304")).ExitCode);
        var result = await RunAsync("observability", "--hours", "24");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Requests: 0", result.Output, StringComparison.Ordinal);
        Assert.Contains("Success rate: 0.0%", result.Output, StringComparison.Ordinal);
        Assert.Contains("Top provider: unknown", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("start", "")]
    [InlineData("stop", "")]
    [InlineData("web", "")]
    [InlineData("launch", "")]
    [InlineData("run", "")]
    [InlineData("s", "")]
    [InlineData("svc", "")]
    [InlineData("gw", "")]
    [InlineData("cfg", "")]
    [InlineData("pf", "")]
    [InlineData("rt", "")]
    [InlineData("p", "")]
    [InlineData("i", "")]
    [InlineData("gateway", "serve")]
    [InlineData("gateway", "start")]
    [InlineData("gateway", "stop")]
    [InlineData("gateway", "restart")]
    [InlineData("gateway", "status")]
    [InlineData("provider", "use")]
    [InlineData("service", "up")]
    public async Task Cli_RemovedCommandsFailWithoutSideEffects(string group, string command)
    {
        var arguments = new[] { group, command, "--help" }
            .Where(value => value.Length > 0)
            .ToArray();
        var result = await RunAsync(arguments);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "service.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public async Task Cli_ManagedStatusOptionIsRemoved()
    {
        var result = await RunAsync("status", "--managed");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_directory, "service.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public async Task Cli_SettingsValidationAndPreview_UseGlobalFileDotEnvAndRepeatedOverridesWithoutSaving()
    {
        var path = Path.Combine(_directory, "settings.json");
        const string fixture = """
            { "schemaVersion": 1, "server": { "port": 4601 },
              "providers": { "work": { "endpoint": "https://example.test/v1", "models": ["owner/main", "cheap"],
                "credentials": [{ "id": "primary", "environment": "MISSING_PREVIEW_SECRET" }] } },
              "routing": { "defaultModel": "work/owner/main", "rules": [
                { "id": "review", "task": "review", "model": "work/cheap", "rewrite": { "maxOutputTokens": 64 } }
              ] } }
            """;
        await File.WriteAllTextAsync(path, fixture, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".env"),
            "AR__Server__Port=4602\n",
            TestContext.Current.CancellationToken
        );
        var validated = await RunAsync("config", "validate");
        Assert.Equal(0, validated.ExitCode);
        using (var json = JsonDocument.Parse(validated.Output))
        {
            Assert.Equal(4602, json.RootElement.GetProperty("Port").GetInt32());
            Assert.False(json.RootElement.GetProperty("Applied").GetBoolean());
            Assert.True(json.RootElement.GetProperty("Valid").GetBoolean());
        }
        var preview = await RunAsync(
            "route",
            "preview",
            "--file",
            path,
            "--task",
            "review",
            "--set",
            "Execution:MaxAttempts=2",
            "--set",
            "Execution:DeadlineSeconds=15"
        );
        Assert.Equal(0, preview.ExitCode);
        using (var json = JsonDocument.Parse(preview.Output))
        {
            Assert.Equal("work", json.RootElement.GetProperty("Provider").GetString());
            Assert.Equal("cheap", json.RootElement.GetProperty("Model").GetString());
            Assert.Equal("review", json.RootElement.GetProperty("Rule").GetString());
            Assert.Equal(64, json.RootElement.GetProperty("MaxOutputTokens").GetInt32());
            Assert.Equal(2, json.RootElement.GetProperty("MaxAttempts").GetInt32());
            Assert.Equal(15, json.RootElement.GetProperty("DeadlineSeconds").GetInt32());
        }
        var explicitModel = await RunAsync(
            "route",
            "preview",
            "--file",
            path,
            "--model",
            "work/owner/main",
            "--task",
            "review"
        );
        Assert.Equal(0, explicitModel.ExitCode);
        using (var json = JsonDocument.Parse(explicitModel.Output))
            Assert.Equal("owner/main", json.RootElement.GetProperty("Model").GetString());
        Assert.Equal(
            fixture,
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)
        );
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
        Assert.False(File.Exists(Path.Combine(_directory, "service.json")));
        Assert.DoesNotContain("MISSING_PREVIEW_SECRET", preview.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Server:Port=0")]
    [InlineData("Providers:work:ApiKey=private-synthetic-key")]
    [InlineData("not-an-override")]
    public async Task Cli_SettingsErrors_DoNotExposePrivateInputOrCreateStorage(string value)
    {
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {"schemaVersion":1,"providers":{"work":{"endpoint":"https://example.test/v1","models":["main"],
            "credentials":[{"environment":"MISSING_PREVIEW_SECRET"}]}},"routing":{"defaultModel":"work/main"}}
            """,
            TestContext.Current.CancellationToken
        );
        var result = await RunAsync("config", "validate", "--file", path, "--set", value);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No settings applied", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("private-synthetic-key", result.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_directory, "gateway.sqlite")));
    }

    [Fact]
    public async Task Cli_ConcurrentProviderAddsPreserveAllConfigurationUpdates()
    {
        var ids = new[] { "one", "two", "three", "four" };
        var results = await Task.WhenAll(
            ids.Select(id =>
                RunAsync(
                    "provider",
                    "add",
                    id,
                    "--base-url",
                    "https://example.test/v1",
                    "--models",
                    "model"
                )
            )
        );
        Assert.All(results, result => Assert.Equal(0, result.ExitCode));
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        using var document = JsonDocument.Parse(show.Output);
        var savedIds = document
            .RootElement.GetProperty("Providers")
            .EnumerateObject()
            .Select(provider => provider.Name)
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(ids.OrderBy(id => id), savedIds);
    }

    [Fact]
    public async Task Cli_InvalidPortAndDuplicateProvider_ReturnNonzero()
    {
        Assert.NotEqual(0, (await RunAsync("init", "--port", "65536")).ExitCode);
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "provider",
                    "add",
                    "test",
                    "--base-url",
                    "https://example.test/v1",
                    "--models",
                    "model"
                )
            ).ExitCode
        );
        var duplicate = await RunAsync(
            "provider",
            "add",
            "test",
            "--base-url",
            "https://changed.test/v1",
            "--models",
            "model"
        );
        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.Contains("already exists", duplicate.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_AnthropicProtocolAndDotEnvKey_PersistWithoutExposingSecret()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".env"),
            "DOTENV_TEST_API_KEY=dotenv-private-secret\n",
            TestContext.Current.CancellationToken
        );
        var add = await RunAsync(
            "provider",
            "add",
            "claude",
            "--base-url",
            "https://anthropic.test",
            "--models",
            "native",
            "--protocol",
            "Anthropic",
            "--api-key-env",
            "DOTENV_TEST_API_KEY"
        );
        Assert.Equal(0, add.ExitCode);
        Assert.DoesNotContain("dotenv-private-secret", add.Output, StringComparison.Ordinal);
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        Assert.DoesNotContain("dotenv-private-secret", show.Output, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(show.Output);
        Assert.Equal(
            "Anthropic",
            json.RootElement.GetProperty("Providers")
                .GetProperty("claude")
                .GetProperty("Compatibility")
                .GetString()
        );
        Assert.Equal(
            "https://anthropic.test/",
            json.RootElement.GetProperty("Providers")
                .GetProperty("claude")
                .GetProperty("BaseUrl")
                .GetString()
        );
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _directory,
        };
        startInfo.ArgumentList.Add(typeof(GatewayHost).Assembly.Location);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["AR_CONFIG_DIR"] = _directory;
        startInfo.Environment["OPENAI_API_KEY"] = "cli-upstream-secret";
        startInfo.Environment.Remove("DOTENV_TEST_API_KEY");
        foreach (
            var key in startInfo
                .Environment.Keys.Where(key =>
                    key.StartsWith("AR__", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("AR_SETTINGS_FILE", StringComparison.OrdinalIgnoreCase)
                )
                .ToArray()
        )
            startInfo.Environment.Remove(key);
        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
