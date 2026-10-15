using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentRouter.Core;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Api;

public sealed class ResponsesEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpIngress_AuthenticatesAndAppliesSharedLivePolicy(bool stream)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Configuration["GatewayAuthToken"] = "synthetic-gateway-token";
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var policy = new GatewaySettings
        {
            Providers = new()
            {
                ["work"] = new()
                {
                    Endpoint = "https://provider.test/v1",
                    Models = ["owner/model"],
                    Credentials = [new() { Environment = "SYNTHETIC_KEY" }],
                },
            },
            Routing = new()
            {
                DefaultModel = "work/owner/model",
                Rules =
                [
                    new()
                    {
                        Id = "review",
                        RequestedModel = "review",
                        Task = "review",
                        Model = "work/owner/model",
                        Rewrite = new() { MaxOutputTokens = 17, Temperature = 0.1f },
                    },
                ],
            },
        };
        var options = new ClaudeGatewayOptions
        {
            EnableTelemetry = false,
            RoutingSettings = policy,
            Providers =
            [
                new ClaudeGatewayProvider
                {
                    ProviderName = "work",
                    ProviderType = ProviderType.OpenAI,
                    BaseAddress = "https://provider.test/v1",
                    ApiKey = "synthetic",
                    Models = ["owner/model"],
                },
            ],
            RouteModel = new RouteModel
            {
                DefaultModel = "work/owner/model",
                ThinkingModel = "work/owner/model",
                SearchModel = "work/owner/model",
            },
        };
        builder.AddInfrastructure(options);
        var upstream = new StubClient();
        builder.Services.AddKeyedSingleton<IChatClient>("work/owner/model", upstream);
        await using var app = GatewayApplication.Build(builder);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var request = new
        {
            model = "review",
            input = "SYNTHETIC_PROMPT",
            stream,
            store = false,
            max_output_tokens = 200,
        };
        using var denied = await client.PostAsJsonAsync(
            "/v1/responses",
            request,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(0, upstream.Calls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "synthetic-gateway-token"
        );
        client.DefaultRequestHeaders.Add("X-AR-Task", "review");
        using var response = await client.PostAsJsonAsync(
            "/v1/responses",
            request,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("owner/model", upstream.LastOptions!.ModelId);
        Assert.Equal(17, upstream.LastOptions.MaxOutputTokens);
        Assert.Equal(0.1f, upstream.LastOptions.Temperature);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (stream)
        {
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("event: response.completed", body, StringComparison.Ordinal);
            Assert.Contains("\"model\":\"owner/model\"", body, StringComparison.Ordinal);
        }
        else
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.Equal("response", json.GetProperty("object").GetString());
            Assert.Equal("owner/model", json.GetProperty("model").GetString());
            Assert.Equal(7, json.GetProperty("usage").GetProperty("input_tokens").GetInt64());
            Assert.Equal(
                "OK",
                json.GetProperty("output")[0]
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString()
            );
        }
        using var invalid = await client.PostAsJsonAsync(
            "/v1/responses",
            new
            {
                model = "review",
                input = "hello",
                previous_response_id = "other",
            },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(1, upstream.Calls);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            "invalid_request_error",
            error.GetProperty("error").GetProperty("type").GetString()
        );
        using var forbidden = await client.GetAsync(
            "/management/v1/requests",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    internal sealed class StubClient : IChatClient
    {
        public int Calls { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            LastOptions = options;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK"))
                {
                    Usage = new()
                    {
                        InputTokenCount = 7,
                        OutputTokenCount = 2,
                        TotalTokenCount = 9,
                    },
                    FinishReason = ChatFinishReason.Stop,
                }
            );
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            Calls++;
            LastOptions = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "OK");
            await Task.Yield();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new UsageContent(
                        new()
                        {
                            InputTokenCount = 7,
                            OutputTokenCount = 2,
                            TotalTokenCount = 9,
                        }
                    ),
                ]
            )
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
