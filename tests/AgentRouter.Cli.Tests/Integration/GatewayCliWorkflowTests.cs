using System.Diagnostics;
using System.Text.Json;
using AgentRouter.Cli;
using Xunit;

namespace AgentRouter.Tests;

public sealed class GatewayCliWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ccg-cli-" + Guid.NewGuid().ToString("N"));

    public GatewayCliWorkflowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Cli_InitProviderRouteAndProfile_PersistAcrossInvocations()
    {
        Assert.Equal(0, (await RunAsync("init", "--port", "49301")).ExitCode);
        Assert.Equal(0, (await RunAsync("provider", "add", "test", "--base-url", "https://example.test/v1", "--models", "small,large")).ExitCode);
        Assert.Equal(0, (await RunAsync("provider", "use", "test/large")).ExitCode);
        Assert.Equal(0, (await RunAsync("profile", "add", "Code Review", "--model", "test/large")).ExitCode);
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        Assert.DoesNotContain("cli-upstream-secret", show.Output, StringComparison.Ordinal);
        var token = await RunAsync("config", "token");
        Assert.Equal(0, token.ExitCode);
        Assert.StartsWith("sk_", token.Output.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain(token.Output.Trim(), show.Output, StringComparison.Ordinal);
        Assert.Equal(token.Output, (await RunAsync("config", "token")).Output);
        using var json = JsonDocument.Parse(show.Output);
        Assert.Equal(49301, json.RootElement.GetProperty("server").GetProperty("proxyPort").GetInt32());
        Assert.Equal("large", json.RootElement.GetProperty("routing").GetProperty("default").GetProperty("model").GetString());
        Assert.Equal("Code Review", json.RootElement.GetProperty("profiles")[0].GetProperty("name").GetString());
        Assert.Contains("Code Review", (await RunAsync("profile", "list")).Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ConcurrentProviderAddsPreserveAllConfigurationUpdates()
    {
        var ids = new[] { "one", "two", "three", "four" };
        var results = await Task.WhenAll(ids.Select(id => RunAsync("provider", "add", id, "--base-url", "https://example.test/v1", "--models", "model")));
        Assert.All(results, result => Assert.Equal(0, result.ExitCode));
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        using var document = JsonDocument.Parse(show.Output);
        var savedIds = document.RootElement.GetProperty("providers").EnumerateObject().Select(provider => provider.Name).OrderBy(id => id).ToArray();
        Assert.Equal(ids.OrderBy(id => id), savedIds);
    }

    [Fact]
    public async Task Cli_InvalidPortAndDuplicateProvider_ReturnNonzero()
    {
        Assert.NotEqual(0, (await RunAsync("init", "--port", "65536")).ExitCode);
        Assert.Equal(0, (await RunAsync("provider", "add", "test", "--base-url", "https://example.test/v1", "--models", "model")).ExitCode);
        var duplicate = await RunAsync("provider", "add", "test", "--base-url", "https://changed.test/v1", "--models", "model");
        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.Contains("already exists", duplicate.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_AnthropicProtocolAndDotEnvKey_PersistWithoutExposingSecret()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".env"), "DOTENV_TEST_API_KEY=dotenv-private-secret\n", TestContext.Current.CancellationToken);
        var add = await RunAsync("provider", "add", "claude", "--base-url", "https://anthropic.test", "--models", "native", "--protocol", "Anthropic", "--api-key-env", "DOTENV_TEST_API_KEY");
        Assert.Equal(0, add.ExitCode);
        Assert.DoesNotContain("dotenv-private-secret", add.Output, StringComparison.Ordinal);
        var show = await RunAsync("config", "show");
        Assert.Equal(0, show.ExitCode);
        Assert.DoesNotContain("dotenv-private-secret", show.Output, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(show.Output);
        Assert.Equal("anthropic", json.RootElement.GetProperty("providers").GetProperty("claude").GetProperty("compatibility").GetString());
        Assert.Equal("https://anthropic.test/", json.RootElement.GetProperty("providers").GetProperty("claude").GetProperty("baseUrl").GetString());
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
        startInfo.Environment["CC_GATEWAY_CONFIG_DIR"] = _directory;
        startInfo.Environment["OPENAI_API_KEY"] = "cli-upstream-secret";
        startInfo.Environment.Remove("DOTENV_TEST_API_KEY");
        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
