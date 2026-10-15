using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentRouter.Core.Api.Converters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Api;

public class MEAIStreamToAnthropicStreamConverterTests
{
    [Fact]
    public async Task FirstUpdateFailure_DoesNotWriteResponseAndDisposesStream()
    {
        var context = CreateContext();
        var disposed = false;
        var failure = new HttpRequestException("private upstream diagnostic");

        var actual = await Assert.ThrowsAsync<HttpRequestException>(() =>
            MEAIStreamToAnthropicStreamConverter.WriteAsync(
                context,
                Updates(),
                "client-model",
                CancellationToken.None
            )
        );

        Assert.Same(failure, actual);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Null(context.Response.ContentType);
        Assert.Empty(context.Response.Headers);
        Assert.True(disposed);

        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            try
            {
                await Task.Yield();
                yield return Fail();
            }
            finally
            {
                disposed = true;
            }
        }

        ChatResponseUpdate Fail() => throw failure;
    }

    [Fact]
    public async Task FirstUpdateCancellation_DoesNotWriteResponse()
    {
        var context = CreateContext();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MEAIStreamToAnthropicStreamConverter.WriteAsync(
                context,
                Updates(),
                "client-model",
                cancellation.Token
            )
        );

        Assert.Equal(0, context.Response.Body.Length);
        Assert.Null(context.Response.ContentType);

        async IAsyncEnumerable<ChatResponseUpdate> Updates(
            [EnumeratorCancellation] CancellationToken token = default
        )
        {
            await Task.Yield();
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "unreachable");
        }
    }

    [Fact]
    public async Task SuccessfulStream_PreservesEventOrderAndClientModel()
    {
        var context = CreateContext();
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            context,
            Updates(),
            "client-model",
            CancellationToken.None
        );

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.StartsWith("event: message_start\n", body);
        Assert.Contains("\"model\":\"client-model\"", body);
        Assert.Contains("\"text\":\"Hello\"", body);
        Assert.EndsWith("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n", body);

        static async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ReportedUsage_IsIndependentOfTextChunking(int chunks)
    {
        var context = CreateContext();
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            context,
            Updates(),
            "client-model",
            TestContext.Current.CancellationToken
        );

        var delta = Events(context)
            .Single(item => item.GetProperty("type").GetString() == "message_delta");
        Assert.Equal(13, delta.GetProperty("usage").GetProperty("input_tokens").GetInt64());
        Assert.Equal(7, delta.GetProperty("usage").GetProperty("output_tokens").GetInt64());
        Assert.Equal(
            "max_tokens",
            delta.GetProperty("delta").GetProperty("stop_reason").GetString()
        );

        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 13 })],
            };
            for (var index = 0; index < chunks; index++)
                yield return new ChatResponseUpdate(ChatRole.Assistant, "text");
            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Length,
                Contents = [new UsageContent(new UsageDetails { OutputTokenCount = 7 })],
            };
        }
    }

    [Fact]
    public async Task UnknownUsage_DoesNotInventTokenCounts()
    {
        var context = CreateContext();
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            context,
            Updates(),
            "client-model",
            TestContext.Current.CancellationToken
        );
        var events = Events(context);
        Assert.Empty(
            events
                .Single(item => item.GetProperty("type").GetString() == "message_start")
                .GetProperty("message")
                .GetProperty("usage")
                .EnumerateObject()
        );
        Assert.Empty(
            events
                .Single(item => item.GetProperty("type").GetString() == "message_delta")
                .GetProperty("usage")
                .EnumerateObject()
        );

        static async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "text");
        }
    }

    [Fact]
    public async Task ToolThenText_RetainsToolStopReasonAndArguments()
    {
        var context = CreateContext();
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            context,
            Updates(),
            "client-model",
            TestContext.Current.CancellationToken
        );
        var events = Events(context);
        var tool = events.Single(item =>
            item.GetProperty("type").GetString() == "content_block_start"
            && item.GetProperty("content_block").GetProperty("type").GetString() == "tool_use"
        );
        Assert.Equal("call_1", tool.GetProperty("content_block").GetProperty("id").GetString());
        Assert.Equal("Read", tool.GetProperty("content_block").GetProperty("name").GetString());
        var arguments = events.Single(item =>
            item.GetProperty("type").GetString() == "content_block_delta"
            && item.GetProperty("delta").GetProperty("type").GetString() == "input_json_delta"
        );
        using var json = JsonDocument.Parse(
            arguments.GetProperty("delta").GetProperty("partial_json").GetString()!
        );
        Assert.Equal("file.txt", json.RootElement.GetProperty("path").GetString());
        Assert.Equal(
            "tool_use",
            events
                .Single(item => item.GetProperty("type").GetString() == "message_delta")
                .GetProperty("delta")
                .GetProperty("stop_reason")
                .GetString()
        );
        Assert.Equal(
            2,
            events.Count(item => item.GetProperty("type").GetString() == "content_block_stop")
        );

        static async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents =
                [
                    new FunctionCallContent(
                        "call_1",
                        "Read",
                        new Dictionary<string, object?> { ["path"] = "file.txt" }
                    ),
                ],
            };
            yield return new ChatResponseUpdate(ChatRole.Assistant, "following text");
        }
    }

    [Fact]
    public async Task FailureAfterOutput_WritesSanitizedErrorWithoutSuccessTerminator()
    {
        var context = CreateContext();
        var disposed = false;
        await MEAIStreamToAnthropicStreamConverter.WriteAsync(
            context,
            Updates(),
            "client-model",
            TestContext.Current.CancellationToken
        );
        var events = Events(context);
        Assert.Equal("text_delta", events[2].GetProperty("delta").GetProperty("type").GetString());
        Assert.Equal("error", events[^1].GetProperty("type").GetString());
        Assert.Equal("api_error", events[^1].GetProperty("error").GetProperty("type").GetString());
        Assert.DoesNotContain(
            events,
            item => item.GetProperty("type").GetString() is "message_stop" or "message_delta"
        );
        Assert.DoesNotContain(
            "private-secret",
            Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())
        );
        Assert.True(disposed);

        async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            try
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "visible");
                throw new HttpRequestException("private-secret");
            }
            finally
            {
                disposed = true;
            }
        }
    }

    [Fact]
    public async Task CancellationAfterOutput_DisposesStreamWithoutSuccessOrError()
    {
        var context = CreateContext();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var disposed = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MEAIStreamToAnthropicStreamConverter.WriteAsync(
                context,
                Updates(),
                "client-model",
                cancellation.Token
            )
        );
        var events = Events(context);
        Assert.Equal("content_block_delta", events[^1].GetProperty("type").GetString());
        Assert.Equal("visible", events[^1].GetProperty("delta").GetProperty("text").GetString());
        Assert.DoesNotContain(
            events,
            item =>
                item.GetProperty("type").GetString() is "message_stop" or "message_delta" or "error"
        );
        Assert.True(disposed);

        async IAsyncEnumerable<ChatResponseUpdate> Updates(
            [EnumeratorCancellation] CancellationToken token = default
        )
        {
            try
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "visible");
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                disposed = true;
            }
        }
    }

    private static List<JsonElement> Events(DefaultHttpContext context) =>
        Encoding
            .UTF8.GetString(((MemoryStream)context.Response.Body).ToArray())
            .Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line =>
            {
                using var json = JsonDocument.Parse(line[6..]);
                return json.RootElement.Clone();
            })
            .ToList();

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
