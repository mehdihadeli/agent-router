using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter.Core;
using AgentRouter.Core.Api.ErrorHandling;
using AgentRouter.Core.Contracts;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Hosting;
using AgentRouter.Core.Providers;
using AgentRouter.Core.Providers.Azure;
using AgentRouter.Core.Providers.DeepSeek;
using AgentRouter.Core.Providers.OpenAI;
using AgentRouter.Core.Routes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Providers;

public class ProviderTransportTests
{
    [Theory]
    [InlineData("api.deepseek.com", "required", false)]
    [InlineData("api.deepseek.com", "required", true)]
    [InlineData("api.deepseek.com", "lookup", false)]
    [InlineData("api.deepseek.com", "lookup", true)]
    [InlineData("api.deepseek.com", "none", false)]
    [InlineData("api.deepseek.com", "none", true)]
    [InlineData("api.deepseek.com", "auto", false)]
    [InlineData("api.deepseek.com", "auto", true)]
    [InlineData("api.deepseek.com.other.test", "required", false)]
    [InlineData("api.deepseek.com.other.test", "required", true)]
    public async Task DeepSeek_ForcedToolsDisableThinkingWithoutChangingToolChoice(
        string host,
        string choice,
        bool stream
    )
    {
        JsonElement captured = default;
        using var transport = new CompatibilityTransport(async request =>
        {
            captured = JsonSerializer.Deserialize<JsonElement>(
                await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    stream
                        ? string.Concat(StreamFrames(ProviderType.OpenAI, tools: false))
                        : """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}]}""",
                    System.Text.Encoding.UTF8,
                    stream ? "text/event-stream" : "application/json"
                ),
            };
        });
        using var http = new HttpClient(transport);
        var clientOptions = new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri("https://" + host + "/v1"),
            Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(http),
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0),
        };
        DeepSeekToolPolicy.ConfigureClient(clientOptions);
        var native = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential("synthetic-key"),
            clientOptions
        );
        using var client = Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(
            native.GetChatClient("native-model")
        );
        var executed = false;
        var options = new Microsoft.Extensions.AI.ChatOptions
        {
            Tools =
            [
                Microsoft.Extensions.AI.AIFunctionFactory.Create(
                    () =>
                    {
                        executed = true;
                        return "not executed";
                    },
                    "lookup"
                ),
            ],
            ToolMode = choice switch
            {
                "required" => Microsoft.Extensions.AI.ChatToolMode.RequireAny,
                "none" => Microsoft.Extensions.AI.ChatToolMode.None,
                "lookup" => Microsoft.Extensions.AI.ChatToolMode.RequireSpecific("lookup"),
                _ => Microsoft.Extensions.AI.ChatToolMode.Auto,
            },
        };
        Microsoft.Extensions.AI.ChatMessage[] messages =
        [
            new(Microsoft.Extensions.AI.ChatRole.User, "Check tool settings"),
        ];
        if (stream)
        {
            await foreach (
                var update in client.GetStreamingResponseAsync(
                    messages,
                    options,
                    TestContext.Current.CancellationToken
                )
            ) { }
        }
        else
            await client.GetResponseAsync(messages, options, TestContext.Current.CancellationToken);
        Assert.False(executed);
        Assert.Equal("native-model", captured.GetProperty("model").GetString());
        Assert.Equal(
            "lookup",
            captured.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString()
        );
        if (choice == "lookup")
            Assert.Equal(
                "lookup",
                captured
                    .GetProperty("tool_choice")
                    .GetProperty("function")
                    .GetProperty("name")
                    .GetString()
            );
        else
            Assert.Equal(choice, captured.GetProperty("tool_choice").GetString());
        if (host == "api.deepseek.com" && choice != "auto")
            Assert.Equal(
                "disabled",
                captured.GetProperty("thinking").GetProperty("type").GetString()
            );
        else
            Assert.False(captured.TryGetProperty("thinking", out _));
    }

    private sealed class CompatibilityTransport(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => respond(request);
    }

    [Theory]
    [InlineData(typeof(OpenAIExtensiosn), typeof(OpenAIApiClientSettings), "AddOpenAIClient")]
    [InlineData(
        typeof(AzureOpenAIExtensions),
        typeof(AzureOpenAIApiClientSettings),
        "AddAzureOpenAIClient"
    )]
    public void ProviderClientRegistration_PreservesThreeArgumentCompositionApi(
        Type registration,
        Type settings,
        string methodName
    )
    {
        var method = registration.GetMethod(
            methodName,
            [typeof(Microsoft.Extensions.Hosting.IHostApplicationBuilder), settings, typeof(object)]
        );
        Assert.NotNull(method);
        Assert.True(method.IsStatic);
        Assert.True(method.GetParameters()[2].IsOptional);
        Assert.Null(method.GetParameters()[2].DefaultValue);
    }

    [Theory]
    [InlineData(ProviderType.OpenAI, 1, false, 503, false, 0)]
    [InlineData(ProviderType.OpenAI, 2, false, 503, false, 0)]
    [InlineData(ProviderType.OpenAI, 2, true, 503, false, 0)]
    [InlineData(ProviderType.OpenAI, 2, false, 503, true, 0)]
    [InlineData(ProviderType.OpenAI, 2, true, 503, true, 0)]
    [InlineData(ProviderType.OpenAI, 3, false, 403, false, 0)]
    [InlineData(ProviderType.Anthropic, 1, false, 503, false, 0)]
    [InlineData(ProviderType.Anthropic, 2, false, 503, false, 0)]
    [InlineData(ProviderType.Anthropic, 2, true, 503, false, 0)]
    [InlineData(ProviderType.Anthropic, 2, false, 503, true, 0)]
    [InlineData(ProviderType.Anthropic, 2, true, 503, true, 0)]
    [InlineData(ProviderType.Anthropic, 3, false, 403, false, 0)]
    [InlineData(ProviderType.Azure, 1, false, 503, false, 0)]
    [InlineData(ProviderType.Azure, 2, false, 503, false, 0)]
    [InlineData(ProviderType.Azure, 2, true, 503, true, 0)]
    [InlineData(ProviderType.Azure, 3, false, 403, false, 0)]
    [InlineData(ProviderType.GitHubModels, 1, false, 503, false, 0)]
    [InlineData(ProviderType.GitHubModels, 2, false, 503, false, 0)]
    [InlineData(ProviderType.GitHubModels, 2, true, 503, true, 0)]
    [InlineData(ProviderType.GitHubModels, 3, false, 403, false, 0)]
    [InlineData(ProviderType.OpenAI, 8, false, 503, false, 60)]
    [InlineData(ProviderType.Anthropic, 8, false, 503, false, 60)]
    public async Task Gateway_RetryBudget_CountsInitialAttemptAndSharedRetries(
        ProviderType protocol,
        int maxAttempts,
        bool stream,
        int upstreamStatus,
        bool recoverOnLastAttempt,
        int retryAfterSeconds
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var providerCalls = 0;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages"
                : protocol == ProviderType.Azure
                    ? "/openai/deployments/native-model/chat/completions"
                : "/v1/chat/completions",
            async context =>
            {
                var attempt = Interlocked.Increment(ref providerCalls);
                if (recoverOnLastAttempt && attempt == maxAttempts)
                {
                    if (stream)
                    {
                        context.Response.ContentType = "text/event-stream";
                        foreach (var frame in StreamFrames(protocol, tools: false))
                            await context.Response.WriteAsync(frame, context.RequestAborted);
                    }
                    else
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(
                            protocol == ProviderType.Anthropic
                                ? """{"id":"msg_1","type":"message","role":"assistant","model":"native-model","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":3}}"""
                                : """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}""",
                            context.RequestAborted
                        );
                    }
                    return;
                }
                context.Response.StatusCode = upstreamStatus;
                context.Response.ContentType = "application/json";
                if (retryAfterSeconds > 0)
                    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                else
                    context.Response.Headers["retry-after-ms"] = "1";
                await context.Response.WriteAsync(
                    """{"type":"error","error":{"type":"overloaded_error","message":"synthetic upstream unavailable"}}""",
                    context.RequestAborted
                );
            }
        );
        await upstream.StartAsync(timeout.Token);
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single()
                + (protocol is ProviderType.OpenAI or ProviderType.GitHubModels ? "/v1" : ""),
            "synthetic-retry-key",
            "native-model",
            requestTimeoutSeconds: retryAfterSeconds > 0 ? 1 : 120,
            maxProviderAttempts: maxAttempts
        );
        await gateway.StartAsync(timeout.Token);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                stream,
                messages = new[] { new { role = "user", content = "Hello" } },
            },
            timeout.Token
        );
        if (recoverOnLastAttempt)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (stream)
            {
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                var events = ParseEvents(await response.Content.ReadAsStringAsync(timeout.Token));
                Assert.Equal("message_start", events[0].GetProperty("type").GetString());
                Assert.Equal("message_stop", events[^1].GetProperty("type").GetString());
                Assert.Contains(
                    events,
                    item =>
                        item.GetProperty("type").GetString() == "content_block_delta"
                        && item.GetProperty("delta").GetProperty("text").GetString() == "OK"
                );
            }
            else
            {
                using var json = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(timeout.Token),
                    cancellationToken: timeout.Token
                );
                Assert.Equal(
                    "OK",
                    json.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
                );
                Assert.Equal("end_turn", json.RootElement.GetProperty("stop_reason").GetString());
            }
        }
        else
        {
            Assert.Equal(
                retryAfterSeconds > 0 ? 504
                    : upstreamStatus == 403 ? 403
                    : 529,
                (int)response.StatusCode
            );
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var error = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token
            );
            Assert.Equal(
                upstreamStatus == 403 ? "permission_error" : "overloaded_error",
                error.RootElement.GetProperty("error").GetProperty("type").GetString()
            );
            Assert.Equal(
                retryAfterSeconds > 0 ? "Upstream provider timed out."
                    : upstreamStatus == 403 ? "Upstream permission denied."
                    : "Upstream service unavailable.",
                error.RootElement.GetProperty("error").GetProperty("message").GetString()
            );
        }
        Assert.Equal(
            retryAfterSeconds > 0 || upstreamStatus == 403 ? 1 : maxAttempts,
            Volatile.Read(ref providerCalls)
        );
    }

    [Theory]
    [InlineData(ProviderType.OpenAI, -1)]
    [InlineData(ProviderType.OpenAI, 0)]
    [InlineData(ProviderType.OpenAI, 9)]
    [InlineData(ProviderType.Anthropic, -1)]
    [InlineData(ProviderType.Anthropic, 0)]
    [InlineData(ProviderType.Anthropic, 9)]
    public void Gateway_RetryBudget_RejectsInvalidAttemptCount(ProviderType protocol, int attempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateGateway(
                protocol,
                "http://127.0.0.1:1/v1",
                "synthetic-key",
                "native-model",
                maxProviderAttempts: attempts
            )
        );
    }

    [Theory]
    [InlineData(ProviderType.OpenAI, false, false, false)]
    [InlineData(ProviderType.OpenAI, true, false, false)]
    [InlineData(ProviderType.Anthropic, false, false, false)]
    [InlineData(ProviderType.Anthropic, true, false, false)]
    [InlineData(ProviderType.OpenAI, true, true, false)]
    [InlineData(ProviderType.Anthropic, true, true, false)]
    [InlineData(ProviderType.OpenAI, false, false, true)]
    [InlineData(ProviderType.Anthropic, false, false, true)]
    public async Task Gateway_Deadline_PreservesCancellationAndStreamCommitBoundaries(
        ProviderType protocol,
        bool stream,
        bool outputStarted,
        bool callerCancels
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var providerCalls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions",
            async context =>
            {
                Interlocked.Increment(ref providerCalls);
                started.TrySetResult();
                try
                {
                    if (outputStarted)
                    {
                        context.Response.ContentType = "text/event-stream";
                        foreach (
                            var frame in StreamFrames(protocol, tools: false)
                                .Take(protocol == ProviderType.Anthropic ? 3 : 1)
                        )
                            await context.Response.WriteAsync(frame, context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                }
                catch (OperationCanceledException)
                    when (context.RequestAborted.IsCancellationRequested)
                {
                    cancelled.TrySetResult();
                }
            }
        );
        await upstream.StartAsync(timeout.Token);
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""),
            "synthetic-deadline-key",
            "native-model",
            requestTimeoutSeconds: callerCancels ? 30 : 1,
            maxProviderAttempts: 8
        );
        await gateway.StartAsync(timeout.Token);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(
                new
                {
                    model = "test/native-model",
                    max_tokens = 32,
                    stream,
                    messages = new[] { new { role = "user", content = "Hello" } },
                }
            ),
        };
        var pending = http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            caller.Token
        );
        if (callerCancels)
        {
            await started.Task.WaitAsync(timeout.Token);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        else
        {
            using var response = await pending;
            if (outputStarted)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                using var reader = new StreamReader(
                    await response.Content.ReadAsStreamAsync(timeout.Token)
                );
                var lines = new List<string>();
                await Assert.ThrowsAnyAsync<IOException>(async () =>
                {
                    while (await reader.ReadLineAsync(timeout.Token) is { } line)
                        lines.Add(line);
                });
                var events = ParseEvents(string.Join('\n', lines));
                Assert.Equal("message_start", events[0].GetProperty("type").GetString());
                Assert.Contains(
                    events,
                    item =>
                        item.GetProperty("type").GetString() == "content_block_delta"
                        && item.GetProperty("delta").GetProperty("text").GetString() == "OK"
                );
                Assert.DoesNotContain(
                    events,
                    item =>
                        item.GetProperty("type").GetString() is "message_stop" or "message_delta"
                );
            }
            else
            {
                Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                using var error = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(timeout.Token),
                    cancellationToken: timeout.Token
                );
                Assert.Equal("error", error.RootElement.GetProperty("type").GetString());
                Assert.Equal(
                    "overloaded_error",
                    error.RootElement.GetProperty("error").GetProperty("type").GetString()
                );
                Assert.Equal(
                    "Upstream provider timed out.",
                    error.RootElement.GetProperty("error").GetProperty("message").GetString()
                );
            }
        }
        await cancelled.Task.WaitAsync(timeout.Token);
        Assert.Equal(1, Volatile.Read(ref providerCalls));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3601)]
    public async Task Gateway_Deadline_RejectsInvalidConfigurationBeforeServing(int seconds)
    {
        await using var gateway = CreateGateway(
            ProviderType.OpenAI,
            "http://127.0.0.1:1/v1",
            "synthetic-key",
            "native-model",
            requestTimeoutSeconds: seconds
        );
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            gateway.StartAsync(TestContext.Current.CancellationToken)
        );
    }

    public static IEnumerable<object[]> IngressRequests()
    {
        foreach (var chunked in new[] { false, true })
        {
            foreach (var stream in new[] { false, true })
            foreach (var offset in new[] { -1, 0, 1 })
                yield return new object[] { chunked, stream, offset, true };
            yield return new object[] { chunked, false, 1, false };
        }
    }

    [Theory]
    [MemberData(nameof(IngressRequests))]
    public async Task Gateway_IngressLimit_RejectsOnlyOversizedAuthenticatedBodies(
        bool chunked,
        bool stream,
        int offset,
        bool authorized
    )
    {
        var providerCalls = 0;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            "/v1/chat/completions",
            async context =>
            {
                Interlocked.Increment(ref providerCalls);
                using var json = await JsonDocument.ParseAsync(context.Request.Body);
                Assert.Equal(
                    "Hello",
                    json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
                );
                if (stream)
                {
                    context.Response.ContentType = "text/event-stream";
                    foreach (var frame in StreamFrames(ProviderType.OpenAI, tools: false))
                        await context.Response.WriteAsync(frame, context.RequestAborted);
                }
                else
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}""",
                        context.RequestAborted
                    );
                }
            }
        );
        await upstream.StartAsync(TestContext.Current.CancellationToken);
        await using var gateway = CreateGateway(
            ProviderType.OpenAI,
            upstream.Urls.Single() + "/v1",
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                stream,
                messages = new[] { new { role = "user", content = "Hello" } },
            }
        );
        var jsonLength = body.Length;
        Array.Resize(ref body, checked((int)GatewayDefaults.MaxInferenceRequestBodyBytes + offset));
        Array.Fill(body, (byte)' ', jsonLength, body.Length - jsonLength);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Headers.Add("x-api-key", authorized ? "gateway-secret" : "invalid-secret");
        request.Headers.TransferEncodingChunked = chunked;
        request.Headers.ExpectContinue = true;
        request.Content = chunked ? new UnknownLengthContent(body) : new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            chunked ? "application/vnd.agent+json" : "application/json"
        );
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        var expected =
            !authorized ? 401
            : offset > 0 ? 413
            : 200;
        Assert.Equal(expected, (int)response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        if (expected == 200)
        {
            Assert.Equal(1, Volatile.Read(ref providerCalls));
            if (stream)
            {
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(
                    "message_stop",
                    ParseEvents(responseBody)[^1].GetProperty("type").GetString()
                );
            }
            else
            {
                using var json = JsonDocument.Parse(responseBody);
                Assert.Equal(
                    "OK",
                    json.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
                );
            }
        }
        else
        {
            Assert.Equal(0, Volatile.Read(ref providerCalls));
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(responseBody);
            Assert.Equal("error", json.RootElement.GetProperty("type").GetString());
            Assert.Equal(
                authorized ? "invalid_request_error" : "authentication_error",
                json.RootElement.GetProperty("error").GetProperty("type").GetString()
            );
            Assert.Equal(
                authorized ? "Request body is too large." : "Invalid gateway API key.",
                json.RootElement.GetProperty("error").GetProperty("message").GetString()
            );
            Assert.DoesNotContain("message_start", responseBody, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("{private-secret", "application/json", 400)]
    [InlineData("", "application/json", 400)]
    [InlineData("private-secret", "text/plain", 415)]
    public async Task Gateway_InvalidBody_ReturnsTypedErrorBeforeProvider(
        string body,
        string contentType,
        int status
    )
    {
        var providerCalls = 0;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            "/v1/chat/completions",
            () =>
            {
                Interlocked.Increment(ref providerCalls);
                return Results.Ok();
            }
        );
        await upstream.StartAsync(TestContext.Current.CancellationToken);
        await using var gateway = CreateGateway(
            ProviderType.OpenAI,
            upstream.Urls.Single() + "/v1",
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var content = new StringContent(body, System.Text.Encoding.UTF8, contentType);
        using var response = await http.PostAsync(
            "/v1/messages/",
            content,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        using var json = JsonDocument.Parse(responseBody);
        Assert.Equal("error", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "invalid_request_error",
            json.RootElement.GetProperty("error").GetProperty("type").GetString()
        );
        Assert.DoesNotContain("private-secret", responseBody, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref providerCalls));
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        ) => stream.WriteAsync(body, cancellationToken).AsTask();
    }

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
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions",
            async context =>
            {
                using var json = await JsonDocument.ParseAsync(context.Request.Body);
                captured.Add(json.RootElement.Clone());
                Assert.Equal(
                    protocol == ProviderType.Anthropic
                        ? "transport-secret"
                        : "Bearer transport-secret",
                    context
                        .Request.Headers[
                            protocol == ProviderType.Anthropic ? "x-api-key" : "Authorization"
                        ]
                        .ToString()
                );
                context.Response.ContentType = "application/json";
                var response =
                    protocol == ProviderType.Anthropic
                        ? captured.Count == 1
                            ? """{"id":"msg_1","type":"message","role":"assistant","model":"native-model","content":[{"type":"tool_use","id":"call_1","name":"Read","input":{"path":"file.txt"}}],"stop_reason":"tool_use","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":3}}"""
                            : """{"id":"msg_2","type":"message","role":"assistant","model":"native-model","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":5,"output_tokens":3}}"""
                        : captured.Count == 1
                            ? """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"Read","arguments":"{\"path\":\"file.txt\"}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}"""
                            : """{"id":"chatcmpl-2","object":"chat.completion","created":1,"model":"native-model","choices":[{"index":0,"message":{"role":"assistant","content":"OK"},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":3,"total_tokens":8}}""";
                await context.Response.WriteAsync(response);
            }
        );
        await upstream.StartAsync();
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""),
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        var tools = new[]
        {
            new
            {
                name = "Read",
                description = "Read file",
                input_schema = new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" },
                },
            },
        };
        using var first = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                messages = new[] { new { role = "user", content = "Read file" } },
                tools,
            }
        );
        Assert.True(first.IsSuccessStatusCode, $"Gateway status: {first.StatusCode}");
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal("tool_use", firstJson.RootElement.GetProperty("stop_reason").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            firstJson.RootElement.GetProperty("stop_sequence").ValueKind
        );
        Assert.False(firstJson.RootElement.TryGetProperty("stopReason", out _));
        Assert.False(firstJson.RootElement.TryGetProperty("stopSequence", out _));
        var call = firstJson.RootElement.GetProperty("content")[0];
        Assert.Equal("tool_use", call.GetProperty("type").GetString());
        Assert.Equal("Read", call.GetProperty("name").GetString());
        Assert.Equal("file.txt", call.GetProperty("input").GetProperty("path").GetString());
        var callId = call.GetProperty("id").GetString();
        using var second = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                tools,
                messages = new object[]
                {
                    new { role = "user", content = "Read file" },
                    new
                    {
                        role = "assistant",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_use",
                                id = callId,
                                name = "Read",
                                input = new { path = "file.txt" },
                            },
                        },
                    },
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = callId,
                                content = "file contents",
                            },
                        },
                    },
                },
            }
        );
        Assert.True(second.IsSuccessStatusCode, $"Gateway status: {second.StatusCode}");
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("end_turn", secondJson.RootElement.GetProperty("stop_reason").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            secondJson.RootElement.GetProperty("stop_sequence").ValueKind
        );
        Assert.False(secondJson.RootElement.TryGetProperty("stopReason", out _));
        Assert.False(secondJson.RootElement.TryGetProperty("stopSequence", out _));
        Assert.Equal(
            "OK",
            secondJson.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
        );
        Assert.Equal(2, captured.Count);
        Assert.All(
            captured,
            request => Assert.Equal("native-model", request.GetProperty("model").GetString())
        );
        var tool = captured[0].GetProperty("tools")[0];
        Assert.Equal(
            "Read",
            (protocol == ProviderType.Anthropic ? tool : tool.GetProperty("function"))
                .GetProperty("name")
                .GetString()
        );
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
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions",
            async context =>
            {
                using var request = await JsonDocument.ParseAsync(context.Request.Body);
                Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
                Assert.Equal("native-model", request.RootElement.GetProperty("model").GetString());
                context.Response.ContentType = "text/event-stream";
                foreach (var frame in StreamFrames(protocol, tools: false))
                {
                    await context.Response.WriteAsync(frame, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
        );
        await upstream.StartAsync();
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""),
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                stream = true,
                messages = new[] { new { role = "user", content = "Hello" } },
            }
        );
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: message_start", body, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"OK\"", body, StringComparison.Ordinal);
        Assert.Contains("event: message_stop", body, StringComparison.Ordinal);
        var events = ParseEvents(body);
        var delta = events.Single(item => item.GetProperty("type").GetString() == "message_delta");
        Assert.Equal(5, delta.GetProperty("usage").GetProperty("input_tokens").GetInt64());
        Assert.Equal(7, delta.GetProperty("usage").GetProperty("output_tokens").GetInt64());
        Assert.Equal(
            "max_tokens",
            delta.GetProperty("delta").GetProperty("stop_reason").GetString()
        );
    }

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public async Task Gateway_SdkStreaming_FragmentedToolArgumentsRemainValid(ProviderType protocol)
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions",
            async context =>
            {
                context.Response.ContentType = "text/event-stream";
                foreach (var frame in StreamFrames(protocol, tools: true))
                {
                    await context.Response.WriteAsync(frame, context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
        );
        await upstream.StartAsync(TestContext.Current.CancellationToken);
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""),
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                stream = true,
                messages = new[] { new { role = "user", content = "Read file" } },
                tools = new[]
                {
                    new
                    {
                        name = "Read",
                        input_schema = new
                        {
                            type = "object",
                            properties = new { path = new { type = "string" } },
                        },
                    },
                },
            },
            TestContext.Current.CancellationToken
        );
        Assert.True(response.IsSuccessStatusCode);
        var events = ParseEvents(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var tool = Assert.Single(
            events.Where(item => item.GetProperty("type").GetString() == "content_block_start")
        );
        Assert.Equal("call_1", tool.GetProperty("content_block").GetProperty("id").GetString());
        Assert.Equal("Read", tool.GetProperty("content_block").GetProperty("name").GetString());
        var arguments = string.Concat(
            events
                .Where(item => item.GetProperty("type").GetString() == "content_block_delta")
                .Select(item => item.GetProperty("delta").GetProperty("partial_json").GetString())
        );
        using var parsed = JsonDocument.Parse(arguments);
        Assert.Equal("file.txt", parsed.RootElement.GetProperty("path").GetString());
        Assert.Equal(
            "tool_use",
            events
                .Single(item => item.GetProperty("type").GetString() == "message_delta")
                .GetProperty("delta")
                .GetProperty("stop_reason")
                .GetString()
        );
        Assert.Equal("message_stop", events[^1].GetProperty("type").GetString());
    }

    public static IEnumerable<object[]> RejectedRequests()
    {
        var statuses = new (int Upstream, int Gateway, string Type, string Message)[]
        {
            (400, 400, "invalid_request_error", "Upstream rejected the request."),
            (401, 401, "authentication_error", "Upstream authentication failed."),
            (403, 403, "permission_error", "Upstream permission denied."),
            (404, 404, "not_found_error", "Upstream resource not found."),
            (413, 413, "invalid_request_error", "Upstream request is too large."),
            (422, 400, "invalid_request_error", "Upstream rejected the request."),
            (429, 429, "rate_limit_error", "Upstream rate limit exceeded."),
            (503, 529, "overloaded_error", "Upstream service unavailable."),
            (529, 529, "overloaded_error", "Upstream service unavailable."),
            (408, 504, "overloaded_error", "Upstream provider timed out."),
            (504, 504, "overloaded_error", "Upstream provider timed out."),
            (500, 502, "overloaded_error", "Upstream provider error."),
        };
        foreach (var protocol in new[] { ProviderType.OpenAI, ProviderType.Anthropic })
        foreach (var stream in new[] { false, true })
        foreach (var (upstream, gateway, type, message) in statuses)
            yield return new object[] { protocol, stream, upstream, gateway, type, message };
    }

    [Theory]
    [MemberData(nameof(RejectedRequests))]
    public async Task Gateway_UpstreamRejection_ReturnsSanitizedJsonWithMappedStatus(
        ProviderType protocol,
        bool stream,
        int upstreamStatus,
        int gatewayStatus,
        string errorType,
        string message
    )
    {
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapPost(
            protocol == ProviderType.Anthropic ? "/v1/messages" : "/v1/chat/completions",
            async context =>
            {
                context.Response.StatusCode = upstreamStatus;
                context.Response.ContentType = "application/json";
                context.Response.Headers["retry-after"] = "0";
                context.Response.Headers["retry-after-ms"] = "1";
                context.Response.Headers["x-private-diagnostic"] = "private-secret";
                await context.Response.WriteAsync(
                    """{"type":"error","error":{"type":"invalid_request_error","message":"private-secret https://private/provider"}}""",
                    context.RequestAborted
                );
            }
        );
        await upstream.StartAsync(TestContext.Current.CancellationToken);
        await using var gateway = CreateGateway(
            protocol,
            upstream.Urls.Single() + (protocol == ProviderType.OpenAI ? "/v1" : ""),
            "transport-secret",
            "native-model"
        );
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(15),
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        using var response = await http.PostAsJsonAsync(
            "/v1/messages",
            new
            {
                model = "test/native-model",
                max_tokens = 32,
                stream,
                messages = new[] { new { role = "user", content = "Hello" } },
            },
            TestContext.Current.CancellationToken
        );
        Assert.Equal(gatewayStatus, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("error", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            errorType,
            json.RootElement.GetProperty("error").GetProperty("type").GetString()
        );
        Assert.Equal(
            message,
            json.RootElement.GetProperty("error").GetProperty("message").GetString()
        );
        Assert.DoesNotContain("private-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("https://private", body, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("x-private-diagnostic"));
        Assert.DoesNotContain("message_start", body, StringComparison.Ordinal);
    }

    private static IEnumerable<string> StreamFrames(ProviderType protocol, bool tools)
    {
        if (protocol == ProviderType.Anthropic)
        {
            yield return Frame(
                new
                {
                    type = "message_start",
                    message = new
                    {
                        id = "msg_1",
                        type = "message",
                        role = "assistant",
                        model = "native-model",
                        content = Array.Empty<object>(),
                        stop_reason = (string?)null,
                        usage = new { input_tokens = 5, output_tokens = 0 },
                    },
                },
                "message_start"
            );
            if (tools)
            {
                yield return Frame(
                    new
                    {
                        type = "content_block_start",
                        index = 0,
                        content_block = new
                        {
                            type = "tool_use",
                            id = "call_1",
                            name = "Read",
                            input = new { },
                        },
                    },
                    "content_block_start"
                );
                foreach (var part in new[] { "{\"path\":", "\"file.txt\"}" })
                    yield return Frame(
                        new
                        {
                            type = "content_block_delta",
                            index = 0,
                            delta = new { type = "input_json_delta", partial_json = part },
                        },
                        "content_block_delta"
                    );
            }
            else
            {
                yield return Frame(
                    new
                    {
                        type = "content_block_start",
                        index = 0,
                        content_block = new { type = "text", text = "" },
                    },
                    "content_block_start"
                );
                yield return Frame(
                    new
                    {
                        type = "content_block_delta",
                        index = 0,
                        delta = new { type = "text_delta", text = "OK" },
                    },
                    "content_block_delta"
                );
            }
            yield return Frame(
                new { type = "content_block_stop", index = 0 },
                "content_block_stop"
            );
            yield return Frame(
                new
                {
                    type = "message_delta",
                    delta = new
                    {
                        stop_reason = tools ? "tool_use" : "max_tokens",
                        stop_sequence = (string?)null,
                    },
                    usage = new { output_tokens = 7 },
                },
                "message_delta"
            );
            yield return Frame(new { type = "message_stop" }, "message_stop");
        }
        else
        {
            if (tools)
            {
                yield return Chunk(
                    new
                    {
                        role = "assistant",
                        tool_calls = new[]
                        {
                            new
                            {
                                index = 0,
                                id = "call_1",
                                type = "function",
                                function = new { name = "Read", arguments = "{\"path\":" },
                            },
                        },
                    }
                );
                yield return Chunk(
                    new
                    {
                        tool_calls = new[]
                        {
                            new { index = 0, function = new { arguments = "\"file.txt\"}" } },
                        },
                    }
                );
            }
            else
                yield return Chunk(new { role = "assistant", content = "OK" });
            yield return Chunk(new { }, tools ? "tool_calls" : "length");
            yield return Frame(
                new
                {
                    id = "chatcmpl-1",
                    @object = "chat.completion.chunk",
                    created = 1,
                    model = "native-model",
                    choices = Array.Empty<object>(),
                    usage = new
                    {
                        prompt_tokens = 5,
                        completion_tokens = 7,
                        total_tokens = 12,
                    },
                }
            );
            yield return "data: [DONE]\n\n";
        }

        static string Chunk(object delta, string? finish = null) =>
            Frame(
                new
                {
                    id = "chatcmpl-1",
                    @object = "chat.completion.chunk",
                    created = 1,
                    model = "native-model",
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta,
                            finish_reason = finish,
                        },
                    },
                }
            );
        static string Frame(object data, string? name = null) =>
            (name is null ? "" : $"event: {name}\n")
            + "data: "
            + JsonSerializer.Serialize(data)
            + "\n\n";
    }

    private static List<JsonElement> ParseEvents(string body) =>
        body.Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line =>
            {
                using var json = JsonDocument.Parse(line[6..]);
                return json.RootElement.Clone();
            })
            .ToList();

    internal static WebApplication CreateGateway(
        ProviderType protocol,
        string endpoint,
        string apiKey,
        string model,
        int requestTimeoutSeconds = 120,
        int maxProviderAttempts = 1
    )
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
        builder.AddAIProviderChatClient(
            new ClaudeGatewayOptions
            {
                EnableTelemetry = false,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                MaxProviderAttempts = maxProviderAttempts,
                Providers =
                [
                    new ClaudeGatewayProvider
                    {
                        ProviderName = "test",
                        ProviderType = protocol,
                        BaseAddress = endpoint,
                        ApiKey = apiKey,
                        Models = [model],
                    },
                ],
                RouteModel = new RouteModel
                {
                    DefaultModel = "test/" + model,
                    ThinkingModel = "test/" + model,
                    SearchModel = "test/" + model,
                },
            }
        );
        return GatewayApplication.Build(builder);
    }
}
