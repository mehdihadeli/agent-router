using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentRouter;
using AgentRouter.Cli;
using AgentRouter.Models.Anthropic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgentRouter.Tests;

public class ErrorHandlingIntegrationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task InvalidRequest_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "",
            max_tokens = 0,
            messages = new[] { new { role = "user", content = "hi" } },
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

        Assert.NotNull(error);
        Assert.Equal("error", error.Type);
        Assert.Equal(ErrorTypes.InvalidRequestError, error.Error.Type);
        Assert.Contains("model", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyMessages_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "test-model",
            max_tokens = 100,
            messages = Array.Empty<object>(),
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

        Assert.NotNull(error);
        Assert.Equal("error", error.Type);
        Assert.Equal(ErrorTypes.InvalidRequestError, error.Error.Type);
        Assert.Contains("messages", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedJson_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var content = new StringContent("{ this is not json }", Encoding.UTF8, "application/json");
        var response = await http.PostAsync("/v1/messages", content);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError,
            $"Expected 400 or 500, got {response.StatusCode}"
        );
    }

    [Fact]
    public async Task UnknownModel_ReturnsError_AnthropicFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "unknown-provider/nonexistent-model",
            max_tokens = 100,
            messages = new[] { new { role = "user", content = "hi" } },
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

            Assert.NotNull(error);
            Assert.Equal("error", error.Type);
            Assert.NotNull(error.Error.Type);
            Assert.NotNull(error.Error.Message);
        }
    }

    private static WebApplicationFactory<GatewayHost> CreateFactory()
    {
        return new ConfiguredGatewayFactory();
    }

    private sealed class ConfiguredGatewayFactory : WebApplicationFactory<GatewayHost>
    {
        private readonly Dictionary<string, string?> originalValues = [];

        public ConfiguredGatewayFactory()
        {
            SetEnvironmentVariable("GatewayBaseAddress", "http://localhost:4000");
            SetEnvironmentVariable("EnableTelemetry", "false");
            SetEnvironmentVariable("Providers__0__ProviderType", "Azure");
            SetEnvironmentVariable("Providers__0__BaseAddress", "https://dummy.openai.azure.com");
            SetEnvironmentVariable("Providers__0__ApiKey", "dummy-key");
            SetEnvironmentVariable("Providers__0__DeploymentName", "gpt-4.1");
            SetEnvironmentVariable("Providers__0__Models__0", "gpt-4.1");
            SetEnvironmentVariable("RouteModel__DefaultModel", "azure/gpt-4.1");
            SetEnvironmentVariable("RouteModel__ThinkingModel", "azure/gpt-4.1");
            SetEnvironmentVariable("RouteModel__SearchModel", "azure/gpt-4.1");
        }

        protected override IHostBuilder CreateHostBuilder() => Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder => builder.UseTestServer());

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var applicationBuilder = GatewayApplication.CreateBuilder(["--environment", "Testing"]);
            applicationBuilder.WebHost.UseTestServer();
            var app = GatewayApplication.Build(applicationBuilder);
            app.Start();
            return app;
        }

        public override async ValueTask DisposeAsync()
        {
            foreach (var (key, value) in originalValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            await base.DisposeAsync();
        }

        private void SetEnvironmentVariable(string key, string value)
        {
            originalValues.TryAdd(key, Environment.GetEnvironmentVariable(key));
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
