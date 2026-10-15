using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentRouter.Core.Api.Converters;

public static class ResponsesStreamWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        IAsyncEnumerable<ChatResponseUpdate> stream,
        string model,
        CancellationToken cancellationToken
    )
    {
        await using var updates = stream.GetAsyncEnumerator(cancellationToken);
        var hasUpdate = await updates.MoveNextAsync();
        cancellationToken.ThrowIfCancellationRequested();
        model = hasUpdate ? updates.Current.ModelId ?? model : model;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        var id = "resp_" + Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sequence = 0;
        var output = new List<object>();
        var text = new StringBuilder();
        var textId = "msg_" + Guid.NewGuid().ToString("N");
        var textIndex = -1;
        var reasoning = new StringBuilder();
        var protectedReasoning = new StringBuilder();
        var reasoningId = "rs_" + Guid.NewGuid().ToString("N");
        var reasoningIndex = -1;
        var calls = new HashSet<string>(StringComparer.Ordinal);
        var usage = new UsageDetails();
        ChatFinishReason? finish = null;

        async Task Emit(string type, Dictionary<string, object?> fields)
        {
            fields["type"] = type;
            fields["sequence_number"] = sequence++;
            await context.Response.WriteAsync(
                $"event: {type}\ndata: {JsonSerializer.Serialize(fields)}\n\n",
                cancellationToken
            );
            await context.Response.Body.FlushAsync(cancellationToken);
        }

        object Envelope(string status, string? incomplete = null) =>
            ResponsesConverter.Envelope(id, createdAt, model, status, output, usage, incomplete);

        await Emit("response.created", new() { ["response"] = Envelope("in_progress") });
        await Emit("response.in_progress", new() { ["response"] = Envelope("in_progress") });
        try
        {
            while (hasUpdate)
            {
                var update = updates.Current;
                finish = update.FinishReason ?? finish;
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent delta:
                            if (reasoningIndex < 0)
                            {
                                reasoningIndex = output.Count;
                                var item = ResponsesConverter.ReasoningItem(
                                    reasoningId,
                                    "",
                                    "in_progress"
                                );
                                output.Add(item);
                                await Emit(
                                    "response.output_item.added",
                                    new() { ["output_index"] = reasoningIndex, ["item"] = item }
                                );
                            }
                            reasoning.Append(delta.Text);
                            protectedReasoning.Append(delta.ProtectedData);
                            if (
                                reasoning.Length > 8 * 1024 * 1024
                                || protectedReasoning.Length > 8 * 1024 * 1024
                            )
                                throw new InvalidOperationException(
                                    "Responses reasoning limit exceeded."
                                );
                            if (delta.Text.Length > 0)
                                await Emit(
                                    "response.reasoning_text.delta",
                                    new()
                                    {
                                        ["output_index"] = reasoningIndex,
                                        ["item_id"] = reasoningId,
                                        ["content_index"] = 0,
                                        ["delta"] = delta.Text,
                                    }
                                );
                            break;
                        case TextContent delta:
                            if (textIndex < 0)
                            {
                                textIndex = output.Count;
                                var item = ResponsesConverter.TextItem(textId, "", "in_progress");
                                output.Add(item);
                                await Emit(
                                    "response.output_item.added",
                                    new() { ["output_index"] = textIndex, ["item"] = item }
                                );
                                await Emit(
                                    "response.content_part.added",
                                    new()
                                    {
                                        ["output_index"] = textIndex,
                                        ["item_id"] = textId,
                                        ["content_index"] = 0,
                                        ["part"] = ResponsesConverter.TextPart(""),
                                    }
                                );
                            }
                            text.Append(delta.Text);
                            if (text.Length > 8 * 1024 * 1024)
                                throw new InvalidOperationException(
                                    "Responses output limit exceeded."
                                );
                            await Emit(
                                "response.output_text.delta",
                                new()
                                {
                                    ["output_index"] = textIndex,
                                    ["item_id"] = textId,
                                    ["content_index"] = 0,
                                    ["delta"] = delta.Text,
                                }
                            );
                            break;
                        case FunctionCallContent call:
                            if (!calls.Add(call.CallId))
                                throw new InvalidOperationException(
                                    "Repeated upstream function call ID."
                                );
                            var index = output.Count;
                            var itemId = "fc_" + Guid.NewGuid().ToString("N");
                            var added = new
                            {
                                id = itemId,
                                type = "function_call",
                                status = "in_progress",
                                call_id = call.CallId,
                                name = call.Name,
                                arguments = "",
                            };
                            await Emit(
                                "response.output_item.added",
                                new() { ["output_index"] = index, ["item"] = added }
                            );
                            var arguments = JsonSerializer.Serialize(
                                call.Arguments ?? new Dictionary<string, object?>()
                            );
                            await Emit(
                                "response.function_call_arguments.delta",
                                new()
                                {
                                    ["output_index"] = index,
                                    ["item_id"] = itemId,
                                    ["delta"] = arguments,
                                }
                            );
                            await Emit(
                                "response.function_call_arguments.done",
                                new()
                                {
                                    ["output_index"] = index,
                                    ["item_id"] = itemId,
                                    ["arguments"] = arguments,
                                }
                            );
                            var completed = ResponsesConverter.FunctionItem(
                                itemId,
                                call,
                                "completed"
                            );
                            output.Add(completed);
                            await Emit(
                                "response.output_item.done",
                                new() { ["output_index"] = index, ["item"] = completed }
                            );
                            break;
                        case UsageContent reported:
                            usage.Add(reported.Details);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unsupported upstream Responses content."
                            );
                    }
                }
                hasUpdate = await updates.MoveNextAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (finish is null)
                throw new InvalidOperationException(
                    "Upstream stream ended without a finish reason."
                );
            if (reasoningIndex >= 0)
            {
                var value = reasoning.ToString();
                await Emit(
                    "response.reasoning_text.done",
                    new()
                    {
                        ["output_index"] = reasoningIndex,
                        ["item_id"] = reasoningId,
                        ["content_index"] = 0,
                        ["text"] = value,
                    }
                );
                output[reasoningIndex] = ResponsesConverter.ReasoningItem(
                    reasoningId,
                    value,
                    "completed",
                    protectedReasoning.Length == 0 ? null : protectedReasoning.ToString()
                );
                await Emit(
                    "response.output_item.done",
                    new() { ["output_index"] = reasoningIndex, ["item"] = output[reasoningIndex] }
                );
            }
            if (textIndex >= 0)
            {
                var value = text.ToString();
                await Emit(
                    "response.output_text.done",
                    new()
                    {
                        ["output_index"] = textIndex,
                        ["item_id"] = textId,
                        ["content_index"] = 0,
                        ["text"] = value,
                    }
                );
                await Emit(
                    "response.content_part.done",
                    new()
                    {
                        ["output_index"] = textIndex,
                        ["item_id"] = textId,
                        ["content_index"] = 0,
                        ["part"] = ResponsesConverter.TextPart(value),
                    }
                );
                output[textIndex] = ResponsesConverter.TextItem(textId, value, "completed");
                await Emit(
                    "response.output_item.done",
                    new() { ["output_index"] = textIndex, ["item"] = output[textIndex] }
                );
            }
            var incomplete =
                finish == ChatFinishReason.Length ? "max_output_tokens"
                : finish == ChatFinishReason.ContentFilter ? "content_filter"
                : null;
            var status = incomplete is null ? "completed" : "incomplete";
            await Emit("response." + status, new() { ["response"] = Envelope(status, incomplete) });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Emit(
                "response.failed",
                new()
                {
                    ["response"] = new
                    {
                        id,
                        @object = "response",
                        created_at = createdAt,
                        model,
                        status = "failed",
                        output,
                        error = new
                        {
                            code = "server_error",
                            message = "Upstream stream interrupted or returned unsupported content.",
                        },
                    },
                }
            );
        }
    }
}
