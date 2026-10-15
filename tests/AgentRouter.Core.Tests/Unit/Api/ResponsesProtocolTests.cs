using System.Text;
using System.Text.Json;
using AgentRouter.Core.Api.Converters;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Api;

public sealed class ResponsesProtocolTests
{
    [Fact]
    public void Response_PreservesReasoningAlongsideText()
    {
        var response = new ChatResponse(
            new ChatMessage(
                ChatRole.Assistant,
                [new TextReasoningContent("Check inputs"), new TextContent("OK")]
            )
        )
        {
            FinishReason = ChatFinishReason.Stop,
            ModelId = "owner/model",
        };
        var projected = JsonSerializer.SerializeToElement(
            ResponsesConverter.ConvertResponse(response, "owner/model")
        );
        var output = projected.GetProperty("output");
        Assert.Equal(2, output.GetArrayLength());
        Assert.Equal("reasoning", output[0].GetProperty("type").GetString());
        Assert.Empty(output[0].GetProperty("summary").EnumerateArray());
        Assert.Equal(
            "reasoning_text",
            output[0].GetProperty("content")[0].GetProperty("type").GetString()
        );
        Assert.Equal(
            "Check inputs",
            output[0].GetProperty("content")[0].GetProperty("text").GetString()
        );
        Assert.Equal("OK", output[1].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Stream_PreservesReasoningDeltasProtectedDataAndHistory()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            yield return new(ChatRole.Assistant, [new TextReasoningContent("Check ")]);
            await Task.Yield();
            yield return new(
                ChatRole.Assistant,
                [new TextReasoningContent("inputs") { ProtectedData = "opaque-signature" }]
            );
            yield return new(
                ChatRole.Assistant,
                [new FunctionCallContent("call_1", "lookup", new Dictionary<string, object?>())]
            )
            {
                FinishReason = ChatFinishReason.ToolCalls,
            };
        }
        await ResponsesStreamWriter.WriteAsync(
            context,
            Updates(),
            "owner/model",
            TestContext.Current.CancellationToken
        );
        var events = Encoding
            .UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())
            .Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..]))
            .ToArray();
        Assert.Equal(
            Enumerable.Range(0, events.Length),
            events.Select(item => item.GetProperty("sequence_number").GetInt32())
        );
        Assert.Equal(
            new[] { "Check ", "inputs" },
            events
                .Where(item =>
                    item.GetProperty("type").GetString() == "response.reasoning_text.delta"
                )
                .Select(item => item.GetProperty("delta").GetString())
        );
        Assert.Equal(
            "Check inputs",
            Assert
                .Single(
                    events,
                    item => item.GetProperty("type").GetString() == "response.reasoning_text.done"
                )
                .GetProperty("text")
                .GetString()
        );
        Assert.Equal("response.completed", events[^1].GetProperty("type").GetString());
        var output = events[^1].GetProperty("response").GetProperty("output");
        Assert.Equal("opaque-signature", output[0].GetProperty("encrypted_content").GetString());
        var request = new ResponsesRequest { Model = "owner/model", Input = output };
        var (messages, _) = ResponsesConverter.Convert(request);
        var assistant = Assert.Single(messages);
        Assert.Equal(ChatRole.Assistant, assistant.Role);
        Assert.Equal(2, assistant.Contents.Count);
        var reasoning = Assert.IsType<TextReasoningContent>(assistant.Contents[0]);
        Assert.Equal("Check inputs", reasoning.Text);
        Assert.Equal("opaque-signature", reasoning.ProtectedData);
        Assert.Equal("call_1", Assert.IsType<FunctionCallContent>(assistant.Contents[1]).CallId);
    }

    [Fact]
    public void Request_PreservesInstructionsSchemasCallsResultsAndRequiredChoice()
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>(
            """
            {"model":"work/owner/model","instructions":"Be precise","store":false,
             "input":[{"role":"developer","content":"Check inputs"},
               {"role":"user","content":[{"type":"input_text","text":"Find entry"}]},
               {"type":"function_call","call_id":"call_1","name":"lookup","arguments":"{\"key\":\"alpha\"}"},
               {"type":"function_call_output","call_id":"call_1","output":"found"}],
             "tools":[{"type":"function","name":"lookup","parameters":{"type":"object","properties":{"key":{"type":"string"}},"required":["key"]}}],
             "tool_choice":{"type":"function","name":"lookup"},"max_output_tokens":42,"parallel_tool_calls":false}
            """,
            JsonSerializerOptions.Web
        )!;
        var (messages, options) = ResponsesConverter.Convert(request);
        Assert.Equal("Be precise", messages[0].Text);
        Assert.Equal(new ChatRole("developer"), messages[1].Role);
        Assert.Equal("Find entry", messages[2].Text);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(messages[3].Contents));
        Assert.Equal("call_1", call.CallId);
        Assert.Equal("alpha", Assert.IsType<JsonElement>(call.Arguments!["key"]).GetString());
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(messages[4].Contents));
        Assert.Equal(call.CallId, result.CallId);
        Assert.Equal("found", result.Result);
        Assert.Equal(ChatRole.Tool, messages[4].Role);
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(options.Tools!));
        Assert.False(tool is AIFunction);
        Assert.Equal(
            "string",
            tool.JsonSchema.GetProperty("properties")
                .GetProperty("key")
                .GetProperty("type")
                .GetString()
        );
        Assert.Equal(
            "lookup",
            Assert.IsType<RequiredChatToolMode>(options.ToolMode).RequiredFunctionName
        );
        Assert.Equal(42, options.MaxOutputTokens);
        Assert.False(options.AllowMultipleToolCalls);
    }

    [Theory]
    [InlineData("\"store\":true")]
    [InlineData("\"background\":true")]
    [InlineData("\"previous_response_id\":\"resp_other\"")]
    [InlineData("\"tools\":[{\"type\":\"web_search\"}]")]
    [InlineData("\"tools\":[{\"type\":\"custom\",\"name\":\"patch\"}]")]
    [InlineData("\"tools\":[{\"type\":\"function\",\"name\":\"f\",\"strict\":true}]")]
    [InlineData("\"tool_choice\":\"required\"")]
    public void Request_RejectsUnsupportedSemantics(string property)
    {
        var request = JsonSerializer.Deserialize<ResponsesRequest>(
            "{\"model\":\"m\",\"input\":\"hello\"," + property + "}",
            JsonSerializerOptions.Web
        )!;
        Assert.Throws<BadRequestException>(() => ResponsesConverter.Convert(request));
    }

    [Fact]
    public void Request_RejectsUnknownFieldsInsteadOfDroppingReasoning()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ResponsesRequest>(
                """{"model":"m","input":"hello","reasoning":{"effort":"high"}}""",
                JsonSerializerOptions.Web
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_OrdersTextFunctionUsageAndIncompleteEvents(bool truncated)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hel");
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "lo");
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "call_42",
                        "lookup",
                        new Dictionary<string, object?> { ["key"] = "alpha" }
                    ),
                ]
            );
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new UsageContent(
                        new UsageDetails
                        {
                            InputTokenCount = 7,
                            OutputTokenCount = 3,
                            TotalTokenCount = 10,
                        }
                    ),
                ]
            )
            {
                FinishReason = truncated ? ChatFinishReason.Length : ChatFinishReason.ToolCalls,
            };
        }
        await ResponsesStreamWriter.WriteAsync(
            context,
            Updates(),
            "owner/model",
            TestContext.Current.CancellationToken
        );
        var events = Encoding
            .UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())
            .Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[6..]))
            .ToArray();
        Assert.Equal(
            Enumerable.Range(0, events.Length),
            events.Select(item => item.GetProperty("sequence_number").GetInt32())
        );
        Assert.Equal("response.created", events[0].GetProperty("type").GetString());
        Assert.Equal(
            new[] { "Hel", "lo" },
            events
                .Where(item => item.GetProperty("type").GetString() == "response.output_text.delta")
                .Select(item => item.GetProperty("delta").GetString())
        );
        var final = events[^1];
        Assert.Equal(
            truncated ? "response.incomplete" : "response.completed",
            final.GetProperty("type").GetString()
        );
        var response = final.GetProperty("response");
        Assert.Equal(
            "Hello",
            response
                .GetProperty("output")[0]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
        );
        Assert.Equal(
            "call_42",
            response.GetProperty("output")[1].GetProperty("call_id").GetString()
        );
        Assert.Equal(10, response.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal(
            truncated ? "max_output_tokens" : null,
            truncated
                ? response.GetProperty("incomplete_details").GetProperty("reason").GetString()
                : null
        );
    }

    [Fact]
    public async Task Stream_InterruptionNeverEmitsSuccessOrRawError()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            await Task.Yield();
            throw new HttpRequestException("private-token secret prompt");
        }
        await ResponsesStreamWriter.WriteAsync(
            context,
            Updates(),
            "model",
            TestContext.Current.CancellationToken
        );
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("response.failed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("response.completed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", body, StringComparison.Ordinal);
    }
}
