using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentRouter.Converters;

/// <summary>
/// Converts a Microsoft.Extensions.AI streaming response (<see cref="ChatResponseUpdate"/>)
/// into Anthropic-compatible Server-Sent Events (SSE) written directly to the HTTP response.
///
/// SSE event sequence (Anthropic Messages API):
///   message_start → content_block_start → content_block_delta* →
///   content_block_stop → (repeat for each block) →
///   message_delta → message_stop
/// </summary>
public static class MEAIStreamToAnthropicStreamConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Reads from the MEAI <paramref name="stream"/>, converts each update into
    /// the corresponding Anthropic SSE event, and writes it to <paramref name="httpContext"/>.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext httpContext,
        IAsyncEnumerable<ChatResponseUpdate> stream,
        string model,
        CancellationToken ct
    )
    {
        // Set SSE headers.
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Response.StatusCode = StatusCodes.Status200OK;

        var messageId = "msg_" + Guid.NewGuid().ToString("N");

        // ── message_start ───────────────────────────────────────────────
        await WriteSseEventAsync(
            httpContext,
            "message_start",
            new
            {
                type = "message_start",
                message = new
                {
                    id = messageId,
                    type = "message",
                    role = "assistant",
                    model,
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    usage = new { input_tokens = 0, output_tokens = 0 },
                },
            },
            ct
        );

        // ── Stream content blocks ───────────────────────────────────────
        var state = new StreamState();

        await foreach (var update in stream.WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } textContent:
                        await HandleTextDeltaAsync(httpContext, state, textContent.Text, ct);
                        break;

                    case FunctionCallContent fc:
                        await HandleToolCallAsync(httpContext, state, fc, ct);
                        break;
                }
            }
        }

        // ── Close any open content block ────────────────────────────────
        if (state.InTextBlock || state.InToolBlock)
        {
            await WriteContentBlockStopAsync(httpContext, state.ContentBlockIndex, ct);
        }

        // ── message_delta ───────────────────────────────────────────────
        var stopReason = state.InToolBlock ? "tool_use" : "end_turn";

        await WriteSseEventAsync(
            httpContext,
            "message_delta",
            new
            {
                type = "message_delta",
                delta = new { stop_reason = stopReason },
                usage = new { output_tokens = state.OutputTokens },
            },
            ct
        );

        // ── message_stop ────────────────────────────────────────────────
        await WriteSseEventAsync(httpContext, "message_stop", new { type = "message_stop" }, ct);

        await httpContext.Response.Body.FlushAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Text delta handling
    // ─────────────────────────────────────────────────────────────────────

    private static async Task HandleTextDeltaAsync(
        HttpContext httpContext,
        StreamState state,
        string text,
        CancellationToken ct
    )
    {
        // Close a previous tool block if we were in one.
        if (state.InToolBlock)
        {
            await WriteContentBlockStopAsync(httpContext, state.ContentBlockIndex, ct);
            state.ContentBlockIndex++;
            state.InToolBlock = false;
        }

        // Start a new text block if not already in one.
        if (!state.InTextBlock)
        {
            await WriteSseEventAsync(
                httpContext,
                "content_block_start",
                new
                {
                    type = "content_block_start",
                    index = state.ContentBlockIndex,
                    content_block = new { type = "text", text = "" },
                },
                ct
            );
            state.InTextBlock = true;
        }

        // Emit text delta.
        await WriteSseEventAsync(
            httpContext,
            "content_block_delta",
            new
            {
                type = "content_block_delta",
                index = state.ContentBlockIndex,
                delta = new { type = "text_delta", text },
            },
            ct
        );

        state.OutputTokens++;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tool call handling
    // ─────────────────────────────────────────────────────────────────────

    private static async Task HandleToolCallAsync(
        HttpContext httpContext,
        StreamState state,
        FunctionCallContent fc,
        CancellationToken ct
    )
    {
        // Close a previous text block if we were in one.
        if (state.InTextBlock)
        {
            await WriteContentBlockStopAsync(httpContext, state.ContentBlockIndex, ct);
            state.ContentBlockIndex++;
            state.InTextBlock = false;
        }

        // Close a previous tool block if switching to a different tool call.
        if (state.InToolBlock && state.CurrentToolCallId != fc.CallId)
        {
            await WriteContentBlockStopAsync(httpContext, state.ContentBlockIndex, ct);
            state.ContentBlockIndex++;
            state.InToolBlock = false;
        }

        var toolId = fc.CallId ?? "toolu_" + Guid.NewGuid().ToString("N");
        var inputJson = fc.Arguments is not null ? JsonSerializer.Serialize(fc.Arguments) : "{}";

        // Start a new tool_use block if not already in one.
        if (!state.InToolBlock)
        {
            state.CurrentToolCallId = toolId;

            await WriteSseEventAsync(
                httpContext,
                "content_block_start",
                new
                {
                    type = "content_block_start",
                    index = state.ContentBlockIndex,
                    content_block = new
                    {
                        type = "tool_use",
                        id = toolId,
                        name = fc.Name,
                        input = new { },
                    },
                },
                ct
            );
            state.InToolBlock = true;
        }

        // Emit the tool input as a JSON delta.
        await WriteSseEventAsync(
            httpContext,
            "content_block_delta",
            new
            {
                type = "content_block_delta",
                index = state.ContentBlockIndex,
                delta = new { type = "input_json_delta", partial_json = inputJson },
            },
            ct
        );

        state.OutputTokens++;
    }

    // ─────────────────────────────────────────────────────────────────────
    // SSE writing helpers
    // ─────────────────────────────────────────────────────────────────────

    private static async Task WriteContentBlockStopAsync(
        HttpContext httpContext,
        int index,
        CancellationToken ct
    )
    {
        await WriteSseEventAsync(
            httpContext,
            "content_block_stop",
            new { type = "content_block_stop", index },
            ct
        );
    }

    /// <summary>
    /// Writes a single SSE event:  event: {eventType}\ndata: {json}\n\n
    /// </summary>
    private static async Task WriteSseEventAsync(
        HttpContext httpContext,
        string eventType,
        object data,
        CancellationToken ct
    )
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var line = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await httpContext.Response.Body.WriteAsync(bytes, ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Stream state tracker
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mutable state tracked while iterating through the MEAI stream
    /// to know which Anthropic content block is currently open.
    /// </summary>
    private sealed class StreamState
    {
        public int ContentBlockIndex { get; set; }
        public bool InTextBlock { get; set; }
        public bool InToolBlock { get; set; }
        public string? CurrentToolCallId { get; set; }
        public int OutputTokens { get; set; }
    }
}
