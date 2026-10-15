using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentRouter.Core;
using AgentRouter.Core.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentRouter.Core.Tests.Integration.Providers;

public class LiveProviderTests
{
    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public void Configuration_RootProviderPaths_EnvironmentOverridesDotenvAndJson(
        ProviderType protocol
    )
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ar-live-config-" + Guid.NewGuid().ToString("N")
        );
        var prefix = "AR_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var variables = new Dictionary<string, string>
        {
            [$"{protocol}__Endpoint"] = "https://environment.test/v1",
            [$"{protocol}__ApiKey"] = "synthetic-environment-key",
            [$"{protocol}__Model"] = "owner/environment-model",
            ["LiveTests__DeadlineSeconds"] = "45",
            ["LiveTests__MaxOutputTokens"] = "256",
        };
        try
        {
            Directory.CreateDirectory(directory);
            var envPath = Path.Combine(directory, ".env");
            File.WriteAllText(
                envPath,
                $"{protocol}__Endpoint=https://dotenv.test/v1\n{protocol}__ApiKey=synthetic-dotenv-key\n{protocol}__Model=owner/dotenv-model\n"
            );
            using var jsonStream = new MemoryStream(
                Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        new Dictionary<string, object>
                        {
                            [protocol.ToString()] = new
                            {
                                Enabled = true,
                                Endpoint = "https://json.test/v1",
                                ApiKey = "synthetic-json-key",
                                Model = "owner/json-model",
                            },
                        }
                    )
                )
            );
            using var json = (ConfigurationRoot)
                new ConfigurationBuilder().AddJsonStream(jsonStream).Build();
            Assert.Equal("https://json.test/v1", ValidateSettings(json, protocol).Endpoint);
            using var dotenv = (ConfigurationRoot)
                new ConfigurationBuilder()
                    .AddConfiguration(json)
                    .AddDotEnvFile(envPath, false)
                    .Build();
            var fromDotenv = ValidateSettings(dotenv, protocol);
            Assert.Equal("https://dotenv.test/v1", fromDotenv.Endpoint);
            Assert.Equal("synthetic-dotenv-key", fromDotenv.ApiKey);
            Assert.Equal("owner/dotenv-model", fromDotenv.Model);
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(prefix + variable.Key, variable.Value);
            }
            using var environment = (ConfigurationRoot)
                new ConfigurationBuilder()
                    .AddConfiguration(dotenv)
                    .AddEnvironmentVariables(prefix)
                    .Build();
            var settings = ValidateSettings(environment, protocol);
            Assert.True(environment.GetValue<bool>($"{protocol}:Enabled"));
            Assert.Equal("https://environment.test/v1", settings.Endpoint);
            Assert.Equal("synthetic-environment-key", settings.ApiKey);
            Assert.Equal("owner/environment-model", settings.Model);
            Assert.Equal(45, settings.DeadlineSeconds);
            Assert.Equal(256, settings.MaxOutputTokens);
        }
        finally
        {
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(prefix + variable.Key, null);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [Trait("Category", "Live")]
    [InlineData(ProviderType.OpenAI, false)]
    [InlineData(ProviderType.OpenAI, true)]
    [InlineData(ProviderType.Anthropic, false)]
    [InlineData(ProviderType.Anthropic, true)]
    public async Task ConfiguredProvider_ReturnsTextThroughGateway(
        ProviderType protocol,
        bool stream
    )
    {
        var settings = LoadSettings(protocol);
        using var timeout = CreateTimeout(settings);
        await using var gateway = ProviderTransportTests.CreateGateway(
            protocol,
            settings.Endpoint,
            settings.ApiKey,
            settings.Model
        );
        await gateway.StartAsync(timeout.Token);
        using var http = CreateHttpClient(gateway.Urls.Single());
        var response = await SendAsync(
            http,
            new
            {
                model = "test/" + settings.Model,
                max_tokens = settings.MaxOutputTokens,
                stream,
                system = "Respond briefly with plain text.",
                messages = new[] { new { role = "user", content = "Reply with OK only." } },
            },
            stream,
            timeout.Token
        );
        Assert.True(
            !string.IsNullOrWhiteSpace(response.Text),
            "Provider must return nonempty text."
        );
        Assert.Empty(response.Tools);
        Assert.Contains(response.StopReason, new[] { "end_turn", "max_tokens" });
    }

    [Theory]
    [Trait("Category", "Live")]
    [InlineData(ProviderType.OpenAI, false)]
    [InlineData(ProviderType.OpenAI, true)]
    [InlineData(ProviderType.Anthropic, false)]
    [InlineData(ProviderType.Anthropic, true)]
    public async Task ConfiguredProvider_ToolResultRoundTripPreservesCallId(
        ProviderType protocol,
        bool stream
    )
    {
        var settings = LoadSettings(protocol);
        using var timeout = CreateTimeout(settings);
        await using var gateway = ProviderTransportTests.CreateGateway(
            protocol,
            settings.Endpoint,
            settings.ApiKey,
            settings.Model
        );
        await gateway.StartAsync(timeout.Token);
        using var http = CreateHttpClient(gateway.Urls.Single());
        const string prompt =
            "Call gateway_test_signal exactly once with no arguments. Do not answer until its result is supplied.";
        const string resultMarker = "live-tool-result-7319";
        var tools = new[]
        {
            new
            {
                name = "gateway_test_signal",
                description = "Returns a test marker. Takes no arguments.",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = false,
                },
            },
        };
        var first = await SendAsync(
            http,
            new
            {
                model = "test/" + settings.Model,
                max_tokens = settings.MaxOutputTokens,
                stream,
                system = "Use the supplied tool when requested. After its result arrives, quote the result marker in your reply.",
                tools,
                tool_choice = new { type = "any" },
                messages = new[] { new { role = "user", content = prompt } },
            },
            stream,
            timeout.Token
        );
        Assert.Equal("tool_use", first.StopReason);
        var call = Assert.Single(first.Tools);
        Assert.Equal("gateway_test_signal", call.GetProperty("name").GetString());
        var callId = call.GetProperty("id").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(callId), "Tool call must have an ID.");
        Assert.Equal(JsonValueKind.Object, call.GetProperty("input").ValueKind);
        Assert.Empty(call.GetProperty("input").EnumerateObject());
        var second = await SendAsync(
            http,
            new
            {
                model = "test/" + settings.Model,
                max_tokens = settings.MaxOutputTokens,
                stream,
                system = "The tool has completed. Reply with its result marker only. Do not call tools again.",
                tools,
                tool_choice = new { type = "none" },
                messages = new object[]
                {
                    new { role = "user", content = prompt },
                    new { role = "assistant", content = first.Blocks },
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = callId,
                                content = resultMarker,
                            },
                        },
                    },
                },
            },
            stream,
            timeout.Token
        );
        Assert.Empty(second.Tools);
        Assert.Contains(second.StopReason, new[] { "end_turn", "max_tokens" });
        Assert.True(
            second.Text.Contains(resultMarker, StringComparison.Ordinal),
            "Final reply must contain the synthetic tool-result marker."
        );
    }

    internal sealed record LiveSettings(
        string Endpoint,
        string ApiKey,
        string Model,
        int DeadlineSeconds,
        int MaxOutputTokens
    );

    private sealed record LiveReply(string Text, List<JsonElement> Blocks, string StopReason)
    {
        public IEnumerable<JsonElement> Tools =>
            Blocks.Where(block => block.GetProperty("type").GetString() == "tool_use");
    }

    internal static LiveSettings LoadSettings(ProviderType protocol)
    {
        if (Environment.GetEnvironmentVariable("AR_LIVE_TESTS") != "1")
            Assert.Skip(
                "Paid upstream requests require AR_LIVE_TESTS=1 in process environment, not .env."
            );
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))
        )
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var configuration = (ConfigurationRoot)
            new ConfigurationBuilder()
                .SetBasePath(directory.FullName)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddDotEnvFile(Path.Combine(directory.FullName, ".env"), optional: true)
                .Build();
        var enabled = new Dictionary<ProviderType, LiveSettings>();
        foreach (var candidate in new[] { ProviderType.OpenAI, ProviderType.Anthropic })
            if (configuration.GetValue<bool>($"{candidate}:Enabled"))
                enabled[candidate] = ValidateSettings(configuration, candidate);
        if (!enabled.TryGetValue(protocol, out var settings))
            Assert.Skip($"{protocol}:Enabled is false.");
        return settings!;
    }

    private static LiveSettings ValidateSettings(
        IConfiguration configuration,
        ProviderType protocol
    )
    {
        var section = configuration.GetSection(protocol.ToString());
        var endpoint = section["Endpoint"];
        var key = section["ApiKey"];
        var model = section["Model"];
        if (
            string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(key)
            || string.IsNullOrWhiteSpace(model)
        )
            throw new InvalidOperationException(
                $"{protocol} requires Endpoint, ApiKey and native Model."
            );
        if (
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || (uri.Scheme == "http" && !uri.IsLoopback)
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
        )
            throw new InvalidOperationException(
                "Live endpoint requires HTTPS or loopback HTTP without embedded credentials, query or fragment."
            );
        var deadline = configuration.GetValue("LiveTests:DeadlineSeconds", 60);
        var maxTokens = configuration.GetValue("LiveTests:MaxOutputTokens", 128);
        if (deadline is < 10 or > 180 || maxTokens is < 32 or > 1024)
            throw new InvalidOperationException(
                "Live limits require DeadlineSeconds 10..180 and MaxOutputTokens 32..1024."
            );
        return new(endpoint, key, model, deadline, maxTokens);
    }

    private static CancellationTokenSource CreateTimeout(LiveSettings settings)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.DeadlineSeconds));
        return timeout;
    }

    private static HttpClient CreateHttpClient(string url)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(url),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Add("x-api-key", "gateway-secret");
        return http;
    }

    private static async Task<LiveReply> SendAsync(
        HttpClient http,
        object payload,
        bool stream,
        CancellationToken token
    )
    {
        using var response = await http.PostAsJsonAsync("/v1/messages", payload, token);
        Assert.True(
            response.IsSuccessStatusCode,
            $"Gateway HTTP status: {(int)response.StatusCode}; inspect provider settings privately. Body omitted."
        );
        var content = await response.Content.ReadAsStringAsync(token);
        return ParseReply(content, stream, response.Content.Headers.ContentType?.MediaType);
    }

    private static LiveReply ParseReply(string content, bool stream, string? mediaType)
    {
        if (!stream)
        {
            Assert.Equal("application/json", mediaType);
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            Assert.Equal("message", root.GetProperty("type").GetString());
            Assert.Equal("assistant", root.GetProperty("role").GetString());
            var blocks = root.GetProperty("content")
                .EnumerateArray()
                .Select(block => block.Clone())
                .ToList();
            return new(
                string.Concat(
                    blocks
                        .Where(block => block.GetProperty("type").GetString() == "text")
                        .Select(block => block.GetProperty("text").GetString())
                ),
                blocks,
                root.GetProperty("stop_reason").GetString()!
            );
        }
        Assert.Equal("text/event-stream", mediaType);
        var events = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(frame =>
                string.Join(
                    "\n",
                    frame
                        .Split('\n')
                        .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                        .Select(line => line[5..].TrimStart())
                )
            )
            .Where(data => data.Length > 0)
            .Select(data =>
            {
                using var document = JsonDocument.Parse(data);
                return document.RootElement.Clone();
            })
            .ToList();
        Assert.NotEmpty(events);
        Assert.True(
            events.All(item => item.GetProperty("type").GetString() != "error"),
            "SSE must not contain an error event."
        );
        Assert.Equal("message_start", events[0].GetProperty("type").GetString());
        Assert.Equal("message_stop", events[^1].GetProperty("type").GetString());
        var blocksByIndex = new Dictionary<int, JsonElement>();
        var textByIndex = new Dictionary<int, StringBuilder>();
        var argumentsByIndex = new Dictionary<int, StringBuilder>();
        string? stopReason = null;
        foreach (var item in events)
        {
            switch (item.GetProperty("type").GetString())
            {
                case "content_block_start":
                    var index = item.GetProperty("index").GetInt32();
                    blocksByIndex.Add(index, item.GetProperty("content_block").Clone());
                    textByIndex[index] = new();
                    argumentsByIndex[index] = new();
                    break;
                case "content_block_delta":
                    var delta = item.GetProperty("delta");
                    var blockIndex = item.GetProperty("index").GetInt32();
                    if (delta.GetProperty("type").GetString() == "text_delta")
                        textByIndex[blockIndex].Append(delta.GetProperty("text").GetString());
                    else if (delta.GetProperty("type").GetString() == "input_json_delta")
                        argumentsByIndex[blockIndex]
                            .Append(delta.GetProperty("partial_json").GetString());
                    break;
                case "message_delta":
                    stopReason = item.GetProperty("delta").GetProperty("stop_reason").GetString();
                    break;
            }
        }
        var assembled = new List<JsonElement>();
        foreach (var (index, block) in blocksByIndex.OrderBy(entry => entry.Key))
        {
            if (block.GetProperty("type").GetString() == "text")
                assembled.Add(
                    JsonSerializer.SerializeToElement(
                        new { type = "text", text = textByIndex[index].ToString() }
                    )
                );
            else if (block.GetProperty("type").GetString() == "tool_use")
            {
                using var arguments = JsonDocument.Parse(
                    argumentsByIndex[index].Length == 0
                        ? block.GetProperty("input").GetRawText()
                        : argumentsByIndex[index].ToString()
                );
                assembled.Add(
                    JsonSerializer.SerializeToElement(
                        new
                        {
                            type = "tool_use",
                            id = block.GetProperty("id").GetString(),
                            name = block.GetProperty("name").GetString(),
                            input = arguments.RootElement,
                        }
                    )
                );
            }
        }
        Assert.True(!string.IsNullOrWhiteSpace(stopReason), "SSE must report a finish reason.");
        return new(
            string.Concat(
                textByIndex.OrderBy(entry => entry.Key).Select(entry => entry.Value.ToString())
            ),
            assembled,
            stopReason!
        );
    }

    [Theory]
    [InlineData("Endpoint", "http://remote.test/v1")]
    [InlineData("Endpoint", "ftp://127.0.0.1/v1")]
    [InlineData("Endpoint", "https://private:key@example.test/v1")]
    [InlineData("Endpoint", "https://example.test/v1?key=private")]
    [InlineData("ApiKey", "")]
    [InlineData("Model", "")]
    [InlineData("DeadlineSeconds", "9")]
    [InlineData("MaxOutputTokens", "1025")]
    public void Configuration_RejectsInvalidLiveSettingsBeforeNetwork(string field, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["OpenAI:Endpoint"] = "https://example.test/v1",
            ["OpenAI:ApiKey"] = "synthetic-secret",
            ["OpenAI:Model"] = "owner/model",
        };
        values[
            field is "DeadlineSeconds" or "MaxOutputTokens"
                ? $"LiveTests:{field}"
                : $"OpenAI:{field}"
        ] = value;
        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            ValidateSettings(settings, ProviderType.OpenAI)
        );
        Assert.DoesNotContain("synthetic-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private:key", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.Anthropic)]
    public void Configuration_PreservesNativeModelAndUsesBoundedDefaults(ProviderType protocol)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{protocol}:Endpoint"] = "https://example.test/Case/v1",
            [$"{protocol}:ApiKey"] = "synthetic-secret",
            [$"{protocol}:Model"] = "owner/native-model",
        };
        var result = ValidateSettings(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            protocol
        );
        Assert.Equal("https://example.test/Case/v1", result.Endpoint);
        Assert.Equal("owner/native-model", result.Model);
        Assert.Equal(60, result.DeadlineSeconds);
        Assert.Equal(128, result.MaxOutputTokens);
    }

    [Fact]
    public void ResponseParser_ReconstructsFragmentedToolArgumentsAndTextInOrder()
    {
        const string content = """
            event: message_start
            data: {"type":"message_start"}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call_test","name":"gateway_test_signal","input":{}}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"}"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"test "}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"marker"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":1}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}

            event: message_stop
            data: {"type":"message_stop"}

            """;
        var result = ParseReply(
            content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal),
            true,
            "text/event-stream"
        );
        Assert.Equal("test marker", result.Text);
        Assert.Equal("tool_use", result.StopReason);
        Assert.Equal(2, result.Blocks.Count);
        var call = Assert.Single(result.Tools);
        Assert.Equal("call_test", call.GetProperty("id").GetString());
        Assert.Equal("gateway_test_signal", call.GetProperty("name").GetString());
        Assert.Empty(call.GetProperty("input").EnumerateObject());
        Assert.Equal("test marker", result.Blocks[1].GetProperty("text").GetString());
    }

    [Fact]
    public void ResponseParser_PreservesJsonToolCallAndFinishReason()
    {
        var result = ParseReply(
            """
            {"type":"message","role":"assistant","content":[
              {"type":"tool_use","id":"call_json","name":"gateway_test_signal","input":{}}
            ],"stop_reason":"tool_use"}
            """,
            false,
            "application/json"
        );
        Assert.Equal("", result.Text);
        Assert.Equal("tool_use", result.StopReason);
        var call = Assert.Single(result.Tools);
        Assert.Equal("call_json", call.GetProperty("id").GetString());
        Assert.Equal("gateway_test_signal", call.GetProperty("name").GetString());
        Assert.Empty(call.GetProperty("input").EnumerateObject());
    }

    [Theory]
    [InlineData("error")]
    [InlineData("truncated")]
    public void ResponseParser_RejectsFailedOrIncompleteStreams(string scenario)
    {
        const string start = "event: message_start\ndata: {\"type\":\"message_start\"}\n\n";
        var content =
            scenario == "error" ? start + "event: error\ndata: {\"type\":\"error\"}\n\n" : start;
        var error = Record.Exception(() => ParseReply(content, true, "text/event-stream"));
        Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(error);
    }
}
