using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Contracts.Sessions;
using AgentRouter.Core.Contracts.Stats;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Configuration;
using AgentRouter.Core.Storage.Secrets;
using AgentRouter.Core.Storage.Sqlite;
using AgentRouter.Core.Tests.Support.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Hosting;

public sealed class GatewayManagementTests
{
    private const string ManagementToken = "management-private-token-with-enough-characters";

    [Fact]
    public async Task RequestHistory_RequiresManagementTokenAndReturnsPersistedUnknownUsage()
    {
        using var workspace = new TempWorkspace();
        await using var app = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(ManagementToken, 0, workspace.Paths),
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        var configuration = app.Services.GetRequiredService<GatewayConfigurationService>();
        await configuration.InitializeAsync(49300, TestContext.Current.CancellationToken);
        var document = await configuration.LoadAsync(TestContext.Current.CancellationToken);
        using var history = new GatewayRequestHistory(
            app.Services.GetRequiredService<SqliteConnectionFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GatewayRequestHistory>.Instance
        );
        await history.StartAsync(TestContext.Current.CancellationToken);
        var summary = new GatewayRequestSummary(
            "request-test",
            DateTimeOffset.UtcNow,
            "work/model",
            "work",
            "model",
            12,
            "ok",
            1,
            1,
            null,
            null
        );
        Assert.True(history.TryRecord(summary));
        await history.StopAsync(TestContext.Current.CancellationToken);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var anonymous = await client.GetAsync(
            "/management/v1/requests",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            document.Server.AuthToken
        );
        using var inferenceToken = await client.GetAsync(
            "/management/v1/requests",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, inferenceToken.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ManagementToken
        );
        var result = await client.GetFromJsonAsync<GatewayRequestSummary[]>(
            "/management/v1/requests?limit=1",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(summary, Assert.Single(result!));
        Assert.Null(result![0].InputTokens);
        Assert.Null(result[0].CostUsd);
        var observability = await client.GetFromJsonAsync<GatewayObservabilitySummary>(
            "/management/v1/observability?hours=24",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(observability);
        Assert.Equal(1, observability.Requests);
        Assert.Equal(1, observability.SuccessfulRequests);
        Assert.Equal(0, observability.FailedRequests);
        Assert.Equal(12, observability.AverageLatencyMs);
        Assert.Equal("work", observability.TopProvider);
        Assert.Equal("model", observability.TopModel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gateway_StopDrainsActiveRequestWhileManagementRemainsResponsive(bool stream)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            "/v1/chat/completions",
            async (HttpContext context) =>
            {
                context.Response.ContentType = stream ? "text/event-stream" : "application/json";
                if (stream)
                {
                    await context.Response.WriteAsync(
                        "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"model\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":null}]}\n\n",
                        deadline.Token
                    );
                    await context.Response.Body.FlushAsync(deadline.Token);
                }
                entered.TrySetResult();
                await release.Task.WaitAsync(deadline.Token);
                await context.Response.WriteAsync(
                    stream
                        ? "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"model\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
                        : "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}",
                    deadline.Token
                );
            }
        );
        await upstream.StartAsync(deadline.Token);
        using var workspace = new TempWorkspace();
        using var portReservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        var instanceId = Guid.NewGuid().ToString("N");
        await using var app = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(ManagementToken, 0, workspace.Paths),
            ManagementToken,
            instanceId
        );
        var configuration = app.Services.GetRequiredService<GatewayConfigurationService>();
        await configuration.InitializeAsync(port, deadline.Token);
        await configuration.AddProviderAsync(
            "test",
            upstream.Urls.Single() + "/v1",
            "private-key",
            ["model"],
            deadline.Token
        );
        await app.StartAsync(deadline.Token);
        var lifecycle = app.Services.GetRequiredService<GatewayLifecycleService>();
        var started = await lifecycle.StartGatewayAsync(deadline.Token);
        Assert.Equal("Running", started.State);
        using var inference = new HttpClient { BaseAddress = new Uri(started.Url!) };
        var document = await configuration.LoadAsync(deadline.Token);
        inference.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            document.Server.AuthToken
        );
        var request = inference.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/model",
                max_tokens = 32,
                stream,
                messages = new[] { new { role = "user", content = "Hello" } },
            },
            deadline.Token
        );
        try
        {
            await entered.Task.WaitAsync(deadline.Token);
            var stop = lifecycle.StopGatewayAsync(deadline.Token);
            Assert.False(stop.IsCompleted);
            Assert.Equal("Stopping", lifecycle.Status.State);
            using var management = Client(app.Urls.Single());
            var identity = await management.GetFromJsonAsync<GatewayServiceIdentity>(
                "/management/v1/identity",
                deadline.Token
            );
            Assert.NotNull(identity);
            Assert.Equal(instanceId, identity.InstanceId);
            release.TrySetResult();
            using var response = await request;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(deadline.Token);
            Assert.Contains("OK", body, StringComparison.Ordinal);
            if (stream)
                Assert.Contains("event: message_stop", body, StringComparison.Ordinal);
            Assert.Equal("Stopped", (await stop).State);
        }
        finally
        {
            release.TrySetResult();
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Gateway_ActionsPreserveManagementAndValidateRestartBeforeStopping()
    {
        using var workspace = new TempWorkspace();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var instanceId = Guid.NewGuid().ToString("N");
        await using var app = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(ManagementToken, 0, workspace.Paths),
            ManagementToken,
            instanceId
        );
        var configuration = app.Services.GetRequiredService<GatewayConfigurationService>();
        await configuration.InitializeAsync(port, TestContext.Current.CancellationToken);
        await configuration.AddProviderAsync(
            "test",
            "https://example.test/v1",
            "private-upstream-key",
            ["model"],
            TestContext.Current.CancellationToken
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        using var unauthenticated = new HttpClient { BaseAddress = client.BaseAddress };
        foreach (var action in new[] { "start", "stop", "restart" })
        {
            using var denied = await unauthenticated.PostAsync(
                $"/management/v1/gateway/{action}",
                null,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        var starts = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    client.PostAsync(
                        "/management/v1/gateway/start",
                        null,
                        TestContext.Current.CancellationToken
                    )
                )
        );
        foreach (var response in starts)
        {
            using (response)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var running = await client.GetFromJsonAsync<GatewayLifecycleResponse>(
            "/management/v1/gateway",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(running);
        Assert.Equal("Running", running.State);
        Assert.Equal($"http://127.0.0.1:{port}", running.Url);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var lifecycle = app.Services.GetRequiredService<GatewayLifecycleService>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.RestartGatewayAsync(cancelled.Token)
        );
        Assert.Equal(running, lifecycle.Status);
        var store = app.Services.GetRequiredService<IConfigStore>();
        var document = await configuration.LoadAsync(TestContext.Current.CancellationToken);
        document.Server.ProxyPort = 0;
        await store.SaveAsync(document, TestContext.Current.CancellationToken);
        using var invalid = await client.PostAsync(
            "/management/v1/gateway/restart",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var preserved = await invalid.Content.ReadFromJsonAsync<GatewayLifecycleResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(preserved);
        Assert.Equal("Running", preserved.State);
        Assert.Equal(running.Url, preserved.Url);
        Assert.NotNull(preserved.Error);
        using var inference = new HttpClient { BaseAddress = new Uri(running.Url!) };
        inference.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            document.Server.AuthToken
        );
        using var models = await inference.GetAsync(
            "/v1/models",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, models.StatusCode);
        await configuration.InitializeAsync(port, TestContext.Current.CancellationToken);
        await configuration.UpdateProviderAsync(
            "test",
            "https://example.test/v1",
            null,
            ["model", "new-model"],
            true,
            ProviderCompatibility.OpenAi,
            TestContext.Current.CancellationToken
        );
        using var restarted = await client.PostAsync(
            "/management/v1/gateway/restart",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, restarted.StatusCode);
        var catalog = await inference.GetStringAsync(
            "/v1/models",
            TestContext.Current.CancellationToken
        );
        Assert.Contains("test/new-model", catalog, StringComparison.Ordinal);
        using var stopped = await client.PostAsync(
            "/management/v1/gateway/stop",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        var stoppedState = await stopped.Content.ReadFromJsonAsync<GatewayLifecycleResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(stoppedState);
        Assert.Equal("Stopped", stoppedState.State);
        Assert.Null(stoppedState.Url);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            inference.GetAsync("/v1/models", TestContext.Current.CancellationToken)
        );
        using var occupied = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        occupied.Start();
        using var conflict = await client.PostAsync(
            "/management/v1/gateway/start",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        occupied.Stop();
        using var recovered = await client.PostAsync(
            "/management/v1/gateway/start",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var identity = await client.GetFromJsonAsync<GatewayServiceIdentity>(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(identity);
        Assert.Equal(instanceId, identity.InstanceId);
        await app.StopAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            inference.GetAsync("/v1/models", TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Management_RemainsAvailableAfterProviderlessGatewayStartFailure()
    {
        using var workspace = new TempWorkspace();
        var instanceId = Guid.NewGuid().ToString("N");
        await using var app = GatewayManagementApplication.Build(
            GatewayManagementApplication.CreateBuilder(ManagementToken, 0, workspace.Paths),
            ManagementToken,
            instanceId
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        using var response = await client.PostAsync(
            "/management/v1/gateway/start",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<GatewayLifecycleResponse>(
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(status);
        Assert.Equal("Failed", status.State);
        Assert.Null(status.Url);
        var identity = await client.GetFromJsonAsync<GatewayServiceIdentity>(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(identity);
        Assert.Equal(instanceId, identity.InstanceId);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Management_StartsWithoutProvidersAndReturnsIdentity()
    {
        using var workspace = new TempWorkspace();
        var instanceId = Guid.NewGuid().ToString("N");
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            instanceId
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        var identity = await client.GetFromJsonAsync<GatewayServiceIdentity>(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(identity);
        Assert.Equal(instanceId, identity.InstanceId);
        Assert.Equal(Environment.ProcessId, identity.ProcessId);
        var document = await client.GetStringAsync(
            "/management/v1/config",
            TestContext.Current.CancellationToken
        );
        var configuration = await client.GetFromJsonAsync<ManagementConfigurationResponse>(
            "/management/v1/config",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(configuration);
        Assert.Empty(configuration.Providers);
        Assert.DoesNotContain(ManagementToken, document, StringComparison.Ordinal);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gateway-client-token")]
    public async Task Management_RejectsMissingOrInferenceCredential(string? suppliedToken)
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        if (suppliedToken is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                suppliedToken
            );
        using var response = await client.GetAsync(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Origin", "https://untrusted.test")]
    [InlineData("Host", "untrusted.test")]
    public async Task Management_RejectsUntrustedHostOrOrigin(string header, string value)
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        client.DefaultRequestHeaders.Add(header, value);
        using var response = await client.GetAsync(
            "/management/v1/identity",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Management_ConfigMasksUpstreamAndClientSecrets()
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        var configuration = app.Services.GetRequiredService<GatewayConfigurationService>();
        await configuration.AddProviderAsync(
            "test",
            "https://example.test/v1",
            "upstream-private-secret",
            ["model"],
            TestContext.Current.CancellationToken
        );
        var stored = await configuration.LoadAsync(TestContext.Current.CancellationToken);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        var document = await client.GetStringAsync(
            "/management/v1/config",
            TestContext.Current.CancellationToken
        );
        Assert.DoesNotContain("upstream-private-secret", document, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.Server.AuthToken!, document, StringComparison.Ordinal);
        Assert.Contains("example.test", document, StringComparison.Ordinal);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Management_ShutdownRequiresAuthenticationAndSignalsLifetime()
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStopping.Register(() =>
            stopping.TrySetResult()
        );
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var denied = await client.PostAsync(
            "/management/v1/shutdown",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ManagementToken
        );
        using var accepted = await client.PostAsync(
            "/management/v1/shutdown",
            null,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await stopping.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Management_ProviderProfileAndRouteCrudPreservesAndDeletesSecrets()
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        using var created = await client.PostAsJsonAsync(
            "/management/v1/providers/first",
            new WriteProviderRequest("https://first.test/v1", ["small"], "first-upstream-key"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var updated = await client.PutAsJsonAsync(
            "/management/v1/providers/first",
            new WriteProviderRequest("https://first.test/v1", ["small", "large"]),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var configuration = app.Services.GetRequiredService<GatewayConfigurationService>();
        Assert.Equal(
            "first-upstream-key",
            (await configuration.LoadAsync(TestContext.Current.CancellationToken))
                .Providers["first"]
                .ApiKey
        );
        using var profile = await client.PostAsJsonAsync(
            "/management/v1/profiles/review",
            new WriteProfileRequest("first/large"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Created, profile.StatusCode);
        using var profileUpdated = await client.PutAsJsonAsync(
            "/management/v1/profiles/review",
            new WriteProfileRequest(
                "first/small",
                "claude",
                false,
                Environment: new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = "2048",
                },
                WorkingDirectory: Path.GetFullPath("review-workspace")
            ),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, profileUpdated.StatusCode);
        var savedProfile = Assert.Single(
            (await configuration.LoadAsync(TestContext.Current.CancellationToken)).Profiles
        );
        Assert.Equal("2048", savedProfile.Environment["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]);
        Assert.Equal(Path.GetFullPath("review-workspace"), savedProfile.WorkingDirectory);
        using var blocked = await client.DeleteAsync(
            "/management/v1/providers/first",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var deletedProfile = await client.DeleteAsync(
            "/management/v1/profiles/review",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, deletedProfile.StatusCode);
        using var second = await client.PostAsJsonAsync(
            "/management/v1/providers/second",
            new WriteProviderRequest(
                "https://second.test",
                ["native"],
                "second-key",
                ProviderCompatibility.Anthropic
            ),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var route = await client.PutAsJsonAsync(
            "/management/v1/routes/default",
            new WriteRouteRequest("second/native"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, route.StatusCode);
        using var referenced = await client.DeleteAsync(
            "/management/v1/providers/second",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Conflict, referenced.StatusCode);
        using var deleted = await client.DeleteAsync(
            "/management/v1/providers/first",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var stored = await configuration.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(stored.Providers.ContainsKey("first"));
        Assert.Empty(stored.Profiles!);
        Assert.Equal("second", stored.Routing.Default.ProviderId);
        Assert.Equal("native", stored.Routing.Default.Model);
        Assert.Null(
            await app
                .Services.GetRequiredService<ISecretStore>()
                .GetAsync(SecretKeys.ProviderApiKey("first"), TestContext.Current.CancellationToken)
        );
        using var missing = await client.DeleteAsync(
            "/management/v1/providers/missing",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("file:///local")]
    [InlineData("https://user:password@upstream.test")]
    [InlineData("https://upstream.test?key=secret")]
    public async Task Management_InvalidProviderInputDoesNotPersistOrEchoSecrets(string baseUrl)
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = Client(app.Urls.Single());
        using var response = await client.PostAsJsonAsync(
            "/management/v1/providers/test",
            new WriteProviderRequest(baseUrl, ["model"], "sensitive-upstream-key"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            "sensitive-upstream-key",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal
        );
        Assert.Empty(
            (
                await app
                    .Services.GetRequiredService<GatewayConfigurationService>()
                    .LoadAsync(TestContext.Current.CancellationToken)
            ).Providers
        );
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Management_OpenApiRequiresAuthenticationAndPublishesSafeContracts()
    {
        using var workspace = new TempWorkspace();
        var builder = GatewayManagementApplication.CreateBuilder(
            ManagementToken,
            0,
            workspace.Paths
        );
        await using var app = GatewayManagementApplication.Build(
            builder,
            ManagementToken,
            Guid.NewGuid().ToString("N")
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var denied = await client.GetAsync(
            "/management/v1/openapi/management-v1.json",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ManagementToken
        );
        using var response = await client.GetAsync(
            "/management/v1/openapi/management-v1.json",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        Assert.Equal(
            "v1",
            document.RootElement.GetProperty("info").GetProperty("version").GetString()
        );
        Assert.True(
            document
                .RootElement.GetProperty("paths")
                .GetProperty("/management/v1/providers/{id}")
                .TryGetProperty("put", out _)
        );
        var properties = document
            .RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(ManagementProviderResponse))
            .GetProperty("properties");
        Assert.True(properties.TryGetProperty("HasApiKey", out _));
        Assert.False(properties.TryGetProperty("hasApiKey", out _));
        Assert.False(properties.TryGetProperty("ApiKey", out _));
        Assert.False(properties.TryGetProperty("apiKey", out _));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private static HttpClient Client(string url)
    {
        var client = new HttpClient { BaseAddress = new Uri(url) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ManagementToken
        );
        return client;
    }
}
