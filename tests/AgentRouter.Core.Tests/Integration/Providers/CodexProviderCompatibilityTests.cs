using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentRouter.Core;
using AgentRouter.Core.Api.Converters;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Providers;

[Trait("Category", "Live")]
public class CodexProviderCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnthropicCompatibleProvider_ReturnsTextThroughLiveGateway(bool stream) =>
        new LiveProviderTests().ConfiguredProvider_ReturnsTextThroughGateway(
            ProviderType.Anthropic,
            stream
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OpenAICompatibleProvider_ReturnsTextThroughLiveGateway(bool stream) =>
        new LiveProviderTests().ConfiguredProvider_ReturnsTextThroughGateway(
            ProviderType.OpenAI,
            stream
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnthropicCompatibleProvider_LiveToolRoundTripPreservesCallId(bool stream) =>
        new LiveProviderTests().ConfiguredProvider_ToolResultRoundTripPreservesCallId(
            ProviderType.Anthropic,
            stream
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OpenAICompatibleProvider_LiveToolRoundTripPreservesCallId(bool stream) =>
        new LiveProviderTests().ConfiguredProvider_ToolResultRoundTripPreservesCallId(
            ProviderType.OpenAI,
            stream
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Responses_AnthropicCompatibleProvider_ReturnsText(bool stream) =>
        CheckResponsesAsync(ProviderType.Anthropic, stream, tools: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Responses_OpenAICompatibleProvider_ReturnsText(bool stream) =>
        CheckResponsesAsync(ProviderType.OpenAI, stream, tools: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Responses_AnthropicCompatibleProvider_PreservesFunctionResult(bool stream) =>
        CheckResponsesAsync(ProviderType.Anthropic, stream, tools: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Responses_OpenAICompatibleProvider_PreservesFunctionResult(bool stream) =>
        CheckResponsesAsync(ProviderType.OpenAI, stream, tools: true);

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public async Task ConfiguredProvider_ResponsesProjectionPreservesSdkText(ProviderType protocol)
    {
        var settings = LiveProviderTests.LoadSettings(protocol);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.DeadlineSeconds));
        await using var gateway = ProviderTransportTests.CreateGateway(
            protocol,
            settings.Endpoint,
            settings.ApiKey,
            settings.Model
        );
        await gateway.StartAsync(timeout.Token);
        var client = gateway.Services.GetRequiredKeyedService<IChatClient>(
            "test/" + settings.Model
        );
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(
                [new(ChatRole.User, "Reply with OK only.")],
                new() { ModelId = settings.Model, MaxOutputTokens = settings.MaxOutputTokens },
                timeout.Token
            );
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Assert.Fail(
                $"SDK request failed with {exception.GetType().Name}; provider details omitted."
            );
            return;
        }
        object projection;
        try
        {
            projection = ResponsesConverter.ConvertResponse(response, settings.Model);
        }
        catch (InvalidOperationException)
        {
            var kinds = string.Join(
                ", ",
                response
                    .Messages.SelectMany(message => message.Contents)
                    .Select(content => content.GetType().Name)
                    .Distinct()
            );
            Assert.Fail(
                $"Responses projection rejected SDK content kinds: {kinds}; content omitted."
            );
            return;
        }
        var projected = JsonSerializer.SerializeToElement(projection);
        Assert.Equal(settings.Model, projected.GetProperty("model").GetString());
        var expected = string.Concat(
            response
                .Messages.SelectMany(message => message.Contents)
                .OfType<TextContent>()
                .Select(content => content.Text)
        );
        var actual = string.Concat(
            projected
                .GetProperty("output")
                .EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "message")
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(part => part.GetProperty("type").GetString() == "output_text")
                .Select(part => part.GetProperty("text").GetString())
        );
        Assert.False(
            string.IsNullOrWhiteSpace(expected),
            "Provider must return text; content omitted."
        );
        Assert.True(
            expected == actual,
            "Responses projection must preserve provider text; content omitted."
        );
    }

    private static async Task CheckResponsesAsync(ProviderType protocol, bool stream, bool tools)
    {
        const string marker = "AGENTROUTER_SIGNAL_5937";
        var settings = LiveProviderTests.LoadSettings(protocol);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.DeadlineSeconds));
        await using var gateway = ProviderTransportTests.CreateGateway(
            protocol,
            settings.Endpoint,
            settings.ApiKey,
            settings.Model
        );
        await gateway.StartAsync(timeout.Token);
        using var diagnostics = new SafeDiagnostics();
        gateway.Services.GetRequiredService<ILoggerFactory>().AddProvider(diagnostics);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(gateway.Urls.Single()),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "gateway-secret");
        http.DefaultRequestHeaders.Accept.ParseAdd(
            stream ? "text/event-stream" : "application/json"
        );
        var input = new List<object>
        {
            new
            {
                type = "message",
                role = "user",
                content = new[]
                {
                    new
                    {
                        type = "input_text",
                        text = tools
                            ? $"Call gateway_test_signal with signal '{marker}', then reply with its exact result."
                            : "Reply with OK.",
                    },
                },
            },
        };
        var definitions = tools
            ? new object[]
            {
                new
                {
                    type = "function",
                    name = "gateway_test_signal",
                    description = "Returns the supplied signal. Call once with the required signal argument.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            signal = new { type = "string", @enum = new[] { marker } },
                        },
                        required = new[] { "signal" },
                    },
                    strict = false,
                },
            }
            : [];
        async Task<JsonElement> Send(string choice)
        {
            using var response = await http.PostAsJsonAsync(
                "/v1/responses",
                new
                {
                    model = "test/" + settings.Model,
                    instructions = "Reply briefly; preserve exact tool results.",
                    input,
                    max_output_tokens = settings.MaxOutputTokens,
                    stream,
                    store = false,
                    tools = definitions,
                    tool_choice = choice,
                },
                timeout.Token
            );
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Responses request returned HTTP {(int)response.StatusCode}; {diagnostics.Summary}; response body omitted."
            );
            Assert.Equal(
                stream ? "text/event-stream" : "application/json",
                response.Content.Headers.ContentType!.MediaType
            );
            var reply = ParseResponsesReply(
                await response.Content.ReadAsStringAsync(timeout.Token),
                stream
            );
            Assert.Equal("response", reply.GetProperty("object").GetString());
            Assert.Equal("completed", reply.GetProperty("status").GetString());
            Assert.Equal(settings.Model, reply.GetProperty("model").GetString());
            return reply;
        }
        var result = await Send(tools ? "required" : "none");
        if (tools)
        {
            var call = Assert.Single(
                result.GetProperty("output").EnumerateArray(),
                item => item.GetProperty("type").GetString() == "function_call"
            );
            var callId = call.GetProperty("call_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(callId));
            Assert.Equal("gateway_test_signal", call.GetProperty("name").GetString());
            var arguments = call.GetProperty("arguments").GetString();
            Assert.Equal(
                JsonValueKind.Object,
                JsonSerializer.Deserialize<JsonElement>(arguments!).ValueKind
            );
            var parsedArguments = JsonSerializer.Deserialize<JsonElement>(arguments!);
            Assert.True(
                parsedArguments.TryGetProperty("signal", out var signal),
                "Required function argument missing; model content omitted."
            );
            Assert.True(
                signal.ValueKind == JsonValueKind.String && signal.GetString() == marker,
                "Function argument did not preserve the schema-bound signal; model content omitted."
            );
            input.Add(
                new
                {
                    type = "function_call",
                    call_id = callId,
                    name = "gateway_test_signal",
                    arguments,
                }
            );
            input.Add(
                new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = marker,
                }
            );
            result = await Send("none");
        }
        var text = string.Concat(
            result
                .GetProperty("output")
                .EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "message")
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(part => part.GetProperty("type").GetString() == "output_text")
                .Select(part => part.GetProperty("text").GetString())
        );
        Assert.True(
            text.Contains(tools ? marker : "OK", StringComparison.OrdinalIgnoreCase),
            "Expected response marker missing; model content omitted."
        );
    }

    internal static JsonElement ParseResponsesReply(string body, bool stream)
    {
        if (!stream)
            return JsonSerializer.Deserialize<JsonElement>(body);
        JsonElement? completed = null;
        var previousSequence = -1;
        foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var frame = JsonSerializer.Deserialize<JsonElement>(line[6..]);
            var sequence = frame.GetProperty("sequence_number").GetInt32();
            Assert.True(sequence > previousSequence, "Responses event sequence must increase.");
            previousSequence = sequence;
            var type = frame.GetProperty("type").GetString();
            if (type is "response.failed" or "response.incomplete" or "error")
                throw new InvalidOperationException(
                    "Responses stream failed or was incomplete; upstream body omitted."
                );
            if (type == "response.completed")
            {
                if (completed is not null)
                    throw new InvalidOperationException(
                        "Responses stream has duplicate completion events."
                    );
                completed = frame.GetProperty("response").Clone();
            }
        }
        return completed
            ?? throw new InvalidOperationException("Responses stream ended without completion.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResponsesParser_ExtractsCompletedTextAndFunctionItems(bool stream)
    {
        const string reply = """
            {"object":"response","status":"completed","model":"owner/model","output":[{"type":"message","content":[{"type":"output_text","text":"OK"}]},{"type":"function_call","call_id":"call-real","name":"signal","arguments":"{}"}]}
            """;
        var body = stream
            ? "event: response.created\r\ndata: {\"type\":\"response.created\",\"sequence_number\":0}\r\n\r\nevent: response.completed\r\ndata: {\"type\":\"response.completed\",\"sequence_number\":1,\"response\":"
                + reply.Trim()
                + "}\r\n\r\n"
            : reply;
        var parsed = ParseResponsesReply(body, stream);
        Assert.Equal("owner/model", parsed.GetProperty("model").GetString());
        Assert.Equal(
            "OK",
            parsed
                .GetProperty("output")[0]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
        );
        Assert.Equal(
            "call-real",
            parsed.GetProperty("output")[1].GetProperty("call_id").GetString()
        );
        Assert.Equal("{}", parsed.GetProperty("output")[1].GetProperty("arguments").GetString());
    }

    [Theory]
    [InlineData(
        "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":0,\"delta\":\"partial\"}\n\n"
    )]
    [InlineData("data: {\"type\":\"response.failed\",\"sequence_number\":0}\n\n")]
    public void ResponsesParser_RejectsFailedOrTruncatedStreams(string body)
    {
        Assert.Throws<InvalidOperationException>(() => ParseResponsesReply(body, true));
    }

    [Fact]
    public void LiveDiagnostics_ReportsOnlyAllowedLabelsWithoutProviderSecrets()
    {
        using var diagnostics = new SafeDiagnostics();
        diagnostics
            .CreateLogger("test")
            .Log(
                LogLevel.Error,
                new EventId(1),
                "SYNTHETIC_PRIVATE_BODY",
                new InvalidOperationException(
                    "SYNTHETIC_PRIVATE_KEY at https://private.test with tool_choice in thinking mode"
                ),
                static (state, exception) => state
            );
        Assert.Equal(
            "exception=InvalidOperationException; feature=tool_choice; feature=thinking",
            diagnostics.Summary
        );
        Assert.DoesNotContain("SYNTHETIC_PRIVATE", diagnostics.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("private.test", diagnostics.Summary, StringComparison.Ordinal);
    }

    private sealed class SafeDiagnostics : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _hints = new();
        public string Summary => string.Join("; ", _hints.Distinct());

        public ILogger CreateLogger(string categoryName) => new SafeLogger(this);

        public void Dispose() { }

        private sealed class SafeLogger(SafeDiagnostics owner) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (exception is null)
                    return;
                owner._hints.Enqueue("exception=" + exception.GetType().Name);
                foreach (
                    var label in new[]
                    {
                        "tool_choice",
                        "required",
                        "thinking",
                        "reasoning",
                        "schema",
                        "Unsupported upstream Responses content",
                    }
                )
                    if (exception.Message.Contains(label, StringComparison.OrdinalIgnoreCase))
                        owner._hints.Enqueue("feature=" + label);
            }
        }
    }
}
