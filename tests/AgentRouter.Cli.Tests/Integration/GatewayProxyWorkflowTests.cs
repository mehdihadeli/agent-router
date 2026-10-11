using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentRouter;
using AgentRouter.ErrorHandling;
using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentRouter.Tests;

public sealed class GatewayProxyWorkflowTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    public async Task Proxy_RejectsMissingOrWrongTokenBeforeCallingProvider(string? token)
    {
        var fake = new RecordingChatClient();
        await using var app = CreateApp(fake);
        await app.StartAsync();
        using var http = app.GetTestClient();
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.PostAsJsonAsync("/v1/messages", new { model = "provider/model", max_tokens = 32, messages = new[] { new { role = "user", content = "hello" } } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fake.Calls);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("authentication_error", body.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("Authorization", "Bearer gateway-test-token")]
    [InlineData("x-api-key", "gateway-test-token")]
    public async Task Proxy_ModelCatalogAcceptsBothAuthHeadersAndHealthIsUnambiguous(string header, string value)
    {
        await using var app = CreateApp(new RecordingChatClient());
        await app.StartAsync();
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add(header, value);
        using var response = await http.GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Claude-Code-Gateway")));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("provider/model", json.RootElement.GetProperty("data")[0].GetProperty("id").GetString());
        using var health = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Theory]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("provider/missing")]
    [InlineData("provider/model")]
    public async Task Proxy_ForwardsResolvedBareModelInsteadOfClientAlias(string requestedModel)
    {
        var fake = new RecordingChatClient();
        await using var app = CreateApp(fake);
        await app.StartAsync();
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-test-token");
        using var response = await http.PostAsJsonAsync("/v1/messages", new { model = requestedModel, max_tokens = 32, messages = new[] { new { role = "user", content = "hello" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("model", Assert.Single(fake.Calls).Options!.ModelId);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("OK", json.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Proxy_ForwardsToolDefinitionCallAndResultWithoutExecutingTool()
    {
        var fake = new RecordingChatClient { ReturnToolCall = true };
        await using var app = CreateApp(fake);
        await app.StartAsync();
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-test-token");
        using var first = await http.PostAsJsonAsync("/v1/messages", new
        {
            model = "provider/model",
            max_tokens = 32,
            messages = new[] { new { role = "user", content = "Read file" } },
            tools = new[] { new { name = "Read", description = "Read a file", input_schema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } } },
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Read", Assert.Single(Assert.Single(fake.Calls).Options!.Tools!).Name);
        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var tool = firstBody.RootElement.GetProperty("content")[0];
        Assert.Equal("tool_use", tool.GetProperty("type").GetString());
        Assert.Equal("call-1", tool.GetProperty("id").GetString());
        Assert.Equal("Read", tool.GetProperty("name").GetString());
        fake.ReturnToolCall = false;
        using var second = await http.PostAsJsonAsync("/v1/messages", new
        {
            model = "provider/model",
            max_tokens = 32,
            messages = new object[]
            {
                new { role = "user", content = "Read file" },
                new { role = "assistant", content = new[] { new { type = "tool_use", id = "call-1", name = "Read", input = new { path = "test.txt" } } } },
                new { role = "user", content = new[] { new { type = "tool_result", tool_use_id = "call-1", content = "file contents" } } },
            },
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, fake.Calls.Count);
        var result = Assert.Single(fake.Calls[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        Assert.Contains("file contents", result.Result!.ToString()!, StringComparison.Ordinal);
    }

    private static WebApplication CreateApp(RecordingChatClient fake)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration["GatewayAuthToken"] = "gateway-test-token";
        builder.Services.AddExceptionHandler<AnthropicExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        var options = new ClaudeGatewayOptions
        {
            Providers = [new ClaudeGatewayProvider { ProviderName = "provider", ProviderType = ProviderType.OpenAI, BaseAddress = "https://example.test/v1", ApiKey = "upstream-key", Models = ["model"] }],
            RouteModel = new RouteModel { DefaultModel = "provider/model", ThinkingModel = "provider/model", SearchModel = "provider/model" },
        };
        builder.Services.AddSingleton<IOptions<ClaudeGatewayOptions>>(Options.Create(options));
        builder.Services.AddKeyedSingleton<IChatClient>("provider/model", fake);
        return GatewayApplication.Build(builder);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<(ChatMessage[] Messages, ChatOptions? Options)> Calls { get; } = [];
        public bool ReturnToolCall { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((messages.ToArray(), options));
            var message = ReturnToolCall
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "Read", new Dictionary<string, object?> { ["path"] = "test.txt" })])
                : new ChatMessage(ChatRole.Assistant, "OK");
            return Task.FromResult(new ChatResponse(message) { ModelId = options?.ModelId, FinishReason = ReturnToolCall ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "OK") { FinishReason = ChatFinishReason.Stop };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
