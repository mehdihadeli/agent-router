using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using AgentRouter.Cli;
using AgentRouter.Core.Hosting;
using Xunit;

namespace AgentRouter.Cli.Tests.Integration;

public sealed class GatewayLifecycleTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ccg-lifecycle-" + Guid.NewGuid().ToString("N")
    );
    private string StatePath => Path.Combine(_directory, "service.json");

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Ui_NoOpenStartsManagementOnlyAndReusesVerifiedService()
    {
        var first = await RunAsync("ui", "--no-open", "--management-port", "0");
        Assert.Equal(0, first.ExitCode);
        var state = ReadState();
        Assert.Null(state.GatewayUrl);
        Assert.Contains($"Management UI: {state.Url}/ui/", first.Output, StringComparison.Ordinal);
        Assert.Matches("Pairing code: [A-F0-9]{12}", first.Output);
        var token = await RunAsync("config", "token", "--managed");
        Assert.Equal(0, token.ExitCode);
        Assert.DoesNotContain(token.Output.Trim(), first.Output, StringComparison.Ordinal);
        var second = await RunAsync("ui", "--no-open", "--management-port", "0");
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(state.Identity, ReadState().Identity);
        Assert.Contains($"Management UI: {state.Url}/ui/", second.Output, StringComparison.Ordinal);
        var status = await RunAsync("service", "status");
        Assert.Contains("Inference: Stopped", status.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupedLifecycle_PreservesServiceAndInferenceBoundaries()
    {
        Assert.Equal(0, (await RunAsync("service", "start", "-n", "-p", "0")).ExitCode);
        var state = ReadState();
        Assert.Null(state.GatewayUrl);
        var status = await RunAsync("service", "status");
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Management: Running", status.Output, StringComparison.Ordinal);
        Assert.Contains("Inference: Stopped", status.Output, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("service", "pause")).ExitCode);
        Assert.Equal(state.Identity, ReadState().Identity);
        Assert.Equal(0, (await RunAsync("service", "start", "-n", "-p", "0")).ExitCode);
        Assert.Equal(state.Identity, ReadState().Identity);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
        Assert.False(File.Exists(StatePath));
        Assert.Equal(1, (await RunAsync("service", "status")).ExitCode);
    }

    [Fact]
    public async Task Start_InferenceFailureKeepsManagementAvailableAndReusesIdentity()
    {
        var result = await RunAsync("service", "start", "--management-port", "0");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Inference: Failed", result.Output, StringComparison.Ordinal);
        var state = ReadState();
        Assert.Null(state.GatewayUrl);
        var status = await RunAsync("service", "status");
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Management: Running", status.Output, StringComparison.Ordinal);
        Assert.Contains("Inference: Failed", status.Output, StringComparison.Ordinal);
        Assert.Equal(1, (await RunAsync("service", "resume")).ExitCode);
        Assert.Equal(0, (await RunAsync("service", "start", "--management-port", "0")).ExitCode);
        Assert.Equal(state.Identity, ReadState().Identity);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
    }

    [Fact]
    public async Task Start_ReusesIdentityAndStopRemovesStateWithoutSecrets()
    {
        var first = await RunAsync("service", "start", "--no-gateway", "--management-port", "0");
        Assert.Equal(0, first.ExitCode);
        var state = ReadState();
        Assert.Null(state.GatewayUrl);
        Assert.True(new Uri(state.Url).IsLoopback);
        Assert.NotEqual(0, new Uri(state.Url).Port);
        Assert.Equal(
            0,
            (await RunAsync("service", "start", "--no-gateway", "--management-port", "0")).ExitCode
        );
        Assert.Equal(state, ReadState());
        var status = await RunAsync("service", "status");
        Assert.Equal(0, status.ExitCode);
        Assert.Contains(state.Url, status.Output, StringComparison.Ordinal);
        using var client = new HttpClient();
        using var unauthorized = await client.GetAsync(
            new Uri(new Uri(state.Url), "/management/v1/config"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var raw = await File.ReadAllTextAsync(StatePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
        Assert.False(File.Exists(StatePath));
        Assert.Equal(1, (await RunAsync("service", "status")).ExitCode);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
    }

    [Fact]
    public async Task Start_ConcurrentInvocationsReuseOneService()
    {
        var results = await Task.WhenAll(
            RunAsync("service", "start", "--no-gateway", "--management-port", "0"),
            RunAsync("service", "start", "--no-gateway", "--management-port", "0")
        );
        Assert.All(results, result => Assert.Equal(0, result.ExitCode));
        var state = ReadState();
        Assert.All(
            results,
            result =>
                Assert.Contains(
                    $"Service PID: {state.Identity.ProcessId}",
                    result.Output,
                    StringComparison.Ordinal
                )
        );
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
    }

    [Fact]
    public async Task Start_WithGatewaySeparatesCredentialsAndStopsBothListeners()
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "init",
                    "--port",
                    port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            ).ExitCode
        );
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
                    "model",
                    "--api-key-env",
                    "LIFECYCLE_PROVIDER_KEY"
                )
            ).ExitCode
        );
        Assert.Equal(
            0,
            (await RunAsync("service", "start", "--no-gateway", "--management-port", "0")).ExitCode
        );
        var managementOnly = ReadState();
        Assert.Contains(
            "Inference: Stopped",
            (await RunAsync("service", "status")).Output,
            StringComparison.Ordinal
        );
        Assert.Equal(0, (await RunAsync("service", "start", "--management-port", "0")).ExitCode);
        var state = ReadState();
        Assert.Equal(managementOnly.Identity, state.Identity);
        var liveStatus = await RunAsync("service", "status");
        Assert.Contains($"http://127.0.0.1:{port}", liveStatus.Output, StringComparison.Ordinal);
        var gatewayUrl = $"http://127.0.0.1:{port}";
        Assert.Equal(0, liveStatus.ExitCode);
        Assert.Contains("Inference: Running", liveStatus.Output, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("service", "pause")).ExitCode);
        var paused = await RunAsync("service", "status");
        Assert.Equal(0, paused.ExitCode);
        Assert.Contains("Management: Running", paused.Output, StringComparison.Ordinal);
        Assert.Contains("Inference: Stopped", paused.Output, StringComparison.Ordinal);
        Assert.Equal(state.Identity, ReadState().Identity);
        Assert.Equal(0, (await RunAsync("service", "resume")).ExitCode);
        Assert.Contains(
            "Inference: Running",
            (await RunAsync("service", "status")).Output,
            StringComparison.Ordinal
        );
        int replacementPort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            replacementPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        Assert.Equal(
            0,
            (
                await RunAsync(
                    "init",
                    "--port",
                    replacementPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            ).ExitCode
        );
        var oldGatewayUrl = gatewayUrl;
        gatewayUrl = $"http://127.0.0.1:{replacementPort}";
        var reload = await RunAsync("service", "reload");
        Assert.Equal(0, reload.ExitCode);
        Assert.Contains($"Gateway: {gatewayUrl}", reload.Output, StringComparison.Ordinal);
        var reloadedStatus = await RunAsync("service", "status");
        Assert.Equal(0, reloadedStatus.ExitCode);
        Assert.Contains("Inference: Running", reloadedStatus.Output, StringComparison.Ordinal);
        Assert.Contains($"Gateway: {gatewayUrl}", reloadedStatus.Output, StringComparison.Ordinal);
        Assert.Equal(state.Identity, ReadState().Identity);
        var inferenceToken = await RunAsync("config", "token");
        var managementToken = await RunAsync("config", "token", "--managed");
        Assert.Equal(0, inferenceToken.ExitCode);
        Assert.Equal(0, managementToken.ExitCode);
        Assert.False(inferenceToken.Output.Trim() == managementToken.Output.Trim());
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            inferenceToken.Output.Trim()
        );
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(
                new Uri(new Uri(oldGatewayUrl), "/v1/models"),
                TestContext.Current.CancellationToken
            )
        );
        using var models = await client.GetAsync(
            new Uri(new Uri(gatewayUrl), "/v1/models"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, models.StatusCode);
        Assert.Contains(
            "test/model",
            await models.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        using var deniedManagement = await client.GetAsync(
            new Uri(new Uri(state.Url), "/management/v1/identity"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, deniedManagement.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            managementToken.Output.Trim()
        );
        using var management = await client.GetAsync(
            new Uri(new Uri(state.Url), "/management/v1/identity"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, management.StatusCode);
        using var deniedInference = await client.GetAsync(
            new Uri(new Uri(gatewayUrl), "/v1/models"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, deniedInference.StatusCode);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
        Assert.False(File.Exists(StatePath));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(
                new Uri(new Uri(state.Url), "/management/v1/identity"),
                TestContext.Current.CancellationToken
            )
        );
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync(
                new Uri(new Uri(gatewayUrl), "/v1/models"),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Start_PortConflictCleansFailedChildAndState()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var result = await RunAsync(
            "service",
            "start",
            "--no-gateway",
            "--management-port",
            port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("startup failed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(StatePath));
        Assert.Equal(
            0,
            (await RunAsync("service", "start", "--no-gateway", "--management-port", "0")).ExitCode
        );
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
    }

    [Fact]
    public async Task Stop_RejectsForgedLiveProcessIdentityWithoutKillingProcess()
    {
        using var current = Process.GetCurrentProcess();
        var forged = new GatewayManagedState(
            new GatewayServiceIdentity(
                Guid.NewGuid().ToString("N"),
                current.Id,
                current.StartTime.ToUniversalTime().Ticks,
                "unverified"
            ),
            "http://127.0.0.1:1",
            null
        );
        await File.WriteAllTextAsync(
            StatePath,
            JsonSerializer.Serialize(forged),
            TestContext.Current.CancellationToken
        );
        var result = await RunAsync("service", "stop");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Refusing", result.Output, StringComparison.Ordinal);
        Assert.False(current.HasExited);
        Assert.Equal(forged, ReadState());
        File.Delete(StatePath);
    }

    [Fact]
    public async Task Start_StalePidMarkerDoesNotStopReusedProcess()
    {
        using var current = Process.GetCurrentProcess();
        var stale = new GatewayManagedState(
            new GatewayServiceIdentity(
                Guid.NewGuid().ToString("N"),
                current.Id,
                current.StartTime.ToUniversalTime().Ticks - TimeSpan.FromSeconds(1).Ticks,
                "stale"
            ),
            "http://127.0.0.1:1",
            null
        );
        await File.WriteAllTextAsync(
            StatePath,
            JsonSerializer.Serialize(stale),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            0,
            (await RunAsync("service", "start", "--no-gateway", "--management-port", "0")).ExitCode
        );
        Assert.NotEqual(stale.Identity.InstanceId, ReadState().Identity.InstanceId);
        Assert.False(current.HasExited);
        Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
    }

    [Fact]
    public async Task Web_ForegroundOwnsServiceAndAuthenticatedStopExitsOwner()
    {
        using var process = Process.Start(
            CreateStartInfo(["service", "serve", "--no-gateway", "--management-port", "0"])
        )!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(deadline.Token);
                Assert.NotNull(line);
                if (line.StartsWith("Management listening:", StringComparison.Ordinal))
                    break;
            }
            Assert.Equal(process.Id, ReadState().Identity.ProcessId);
            Assert.Equal(0, (await RunAsync("service", "status")).ExitCode);
            Assert.Equal(0, (await RunAsync("service", "stop")).ExitCode);
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.False(File.Exists(StatePath));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Start_InvalidStateIsNotOverwritten()
    {
        await File.WriteAllTextAsync(StatePath, "not-json", TestContext.Current.CancellationToken);
        var result = await RunAsync("service", "start", "--no-gateway", "--management-port", "0");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("malformed", result.Output, StringComparison.Ordinal);
        Assert.Equal(
            "not-json",
            await File.ReadAllTextAsync(StatePath, TestContext.Current.CancellationToken)
        );
        File.Delete(StatePath);
    }

    private GatewayManagedState ReadState() =>
        JsonSerializer.Deserialize<GatewayManagedState>(File.ReadAllText(StatePath))!;

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _directory,
        };
        start.ArgumentList.Add(typeof(GatewayHost).Assembly.Location);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["AR_CONFIG_DIR"] = _directory;
        start.Environment["LIFECYCLE_PROVIDER_KEY"] = "lifecycle-test-upstream-secret";
        return start;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        using var process = Process.Start(CreateStartInfo(arguments))!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(50));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RunAsync("service", "stop");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
