using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter.Contracts.Configuration;
using AgentRouter.Contracts.Defaults;
using AgentRouter.Storage;
using AgentRouter.Storage.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgentRouter.Tests;

public sealed class GatewayManagementTests
{
    private const string ManagementToken = "management-private-token-with-enough-characters";

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
            new WriteProfileRequest("first/small", "claude", false),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, profileUpdated.StatusCode);
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
        Assert.True(properties.TryGetProperty("hasApiKey", out _));
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
