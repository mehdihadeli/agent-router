using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter;
using AgentRouter.ErrorHandling;
using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;
using AgentRouter.Shared.Contracts;
using AgentRouter.Shared.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentRouter.Providers.OpenAI.Tests;

public class ProviderTransportTests
{
    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public async Task Gateway_SdkTransport_ForwardsModelToolsAndResults(ProviderType protocol)
    {
        var captured = new List<JsonElement>();
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions", async context =>
        {
            using var json = await JsonDocument.ParseAsync(context.Request.Body);
            captured.Add(json.RootElement.Clone());
            Assert.Equal(protocol == ProviderType.Anthropic ? "transport-secret" : "Bearer transport-secret",
                context.Request.Headers[protocol == ProviderType.Anthropic ? "x-api-key" : "Authorization"].ToString());
            context.Response.ContentType = "application/json";
            var response = protocol == ProviderType.Anthropic
                ? captured.Count == 1
                    ? """{"id":"msg_1","type":"message","role":"assistant","model":"native-model","content":[{"type":"tool_use","id":"call_1","name":"Read","input":{"path":"file.txt"}}],"stop_reason":"tool_use","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":3}}"""
                    : """{"id":"msg_2","type":"message","role":"assistant","model":"native-model","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":3}}"""
                : captured.Count == 1
                    ? """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"Read","arguments":"{\"path\":\"file.txt\"}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}"""
                    : """{"id":"chatcmpl-2","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}""";
            await context.Response.WriteAsync(response);
        });
        await upstream.StartAsync();
        await using var gateway = CreateGateway(protocol, upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""), "transport-secret", "native-model");
        await gateway.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        var tools = new[] { new { name = "Read", description = "Read file", input_schema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } } };
        using var first = await http.PostAsJsonAsync("/v1/messages", new
        {
            model = "test/native-model", max_tokens = 32,
            messages = new[] { new { role = "user", content = "Read file" } }, tools,
        });
        Assert.True(first.IsSuccessStatusCode, $"Gateway status: {first.StatusCode}");
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var call = firstJson.RootElement.GetProperty("content")[0];
        Assert.Equal("tool_use", call.GetProperty("type").GetString());
        Assert.Equal("Read", call.GetProperty("name").GetString());
        Assert.Equal("file.txt", call.GetProperty("input").GetProperty("path").GetString());
        var callId = call.GetProperty("id").GetString();
        using var second = await http.PostAsJsonAsync("/v1/messages", new
        {
            model = "test/native-model", max_tokens = 32, tools,
            messages = new object[]
            {
                new { role = "user", content = "Read file" },
                new { role = "assistant", content = new[] { new { type = "tool_use", id = callId, name = "Read", input = new { path = "file.txt" } } } },
                new { role = "user", content = new[] { new { type = "tool_result", tool_use_id = callId, content = "file contents" } } },
            },
        });
        Assert.True(second.IsSuccessStatusCode, $"Gateway status: {second.StatusCode}");
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("OK", secondJson.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(2, captured.Count);
        Assert.All(captured, request => Assert.Equal("native-model", request.GetProperty("model").GetString()));
        var tool = captured[0].GetProperty("tools")[0];
        Assert.Equal("Read", (protocol == ProviderType.Anthropic ? tool : tool.GetProperty("function")).GetProperty("name").GetString());
        if (protocol == ProviderType.Anthropic)
        {
            var result = captured[1].GetProperty("messages")[2].GetProperty("content")[0];
            Assert.Equal("tool_result", result.GetProperty("type").GetString());
            Assert.Equal(callId, result.GetProperty("tool_use_id").GetString());
        }
        else
        {
            var result = captured[1].GetProperty("messages")[2];
            Assert.Equal("tool", result.GetProperty("role").GetString());
            Assert.Equal(callId, result.GetProperty("tool_call_id").GetString());
        }
        Assert.Contains("file contents", captured[1].GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public async Task Gateway_SdkStreaming_ReturnsAnthropicSse(ProviderType protocol)
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions", async context =>
        {
            using var request = await JsonDocument.ParseAsync(context.Request.Body);
            Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("native-model", request.RootElement.GetProperty("model").GetString());
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(protocol == ProviderType.Anthropic
                ? "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"native-model\",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":5,\"output_tokens\":0}}}\n\nevent: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\nevent: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"OK\"}}\n\nevent: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}\n\nevent: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":1}}\n\nevent: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"
                : "data: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"native-model\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":null}]}\n\ndata: {\"id\":\"chatcmpl-1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"native-model\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        });
        await upstream.StartAsync();
        await using var gateway = CreateGateway(protocol, upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""), "transport-secret", "native-model");
        await gateway.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(gateway.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync("/v1/messages", new { model = "test/native-model", max_tokens = 32, stream = true, messages = new[] { new { role = "user", content = "Hello" } } });
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: message_start", body, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"OK\"", body, StringComparison.Ordinal);
        Assert.Contains("event: message_stop", body, StringComparison.Ordinal);
    }

    internal static WebApplication CreateGateway(ProviderType protocol, string endpoint, string apiKey, string model)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["GatewayAuthToken"] = "gateway-secret";
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddExceptionHandler<AnthropicExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.AddAIProviderChatClient(new ClaudeGatewayOptions
        {
            EnableTelemetry = false,
            Providers = [new ClaudeGatewayProvider { ProviderName = "test", ProviderType = protocol, BaseAddress = endpoint, ApiKey = apiKey, Models = [model] }],
            RouteModel = new RouteModel { DefaultModel = "test/" + model, ThinkingModel = "test/" + model, SearchModel = "test/" + model },
        });
        return GatewayApplication.Build(builder);
    }
}
