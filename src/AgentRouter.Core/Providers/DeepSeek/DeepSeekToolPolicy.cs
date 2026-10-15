using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using OpenAI;

namespace AgentRouter.Core.Providers.DeepSeek;

internal sealed class DeepSeekToolPolicy : PipelinePolicy
{
    internal static void ConfigureClient(OpenAIClientOptions options)
    {
        if (
            string.Equals(
                options.Endpoint?.Host,
                "api.deepseek.com",
                StringComparison.OrdinalIgnoreCase
            )
        )
            options.AddPolicy(new DeepSeekToolPolicy(), PipelinePosition.PerCall);
    }

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex
    )
    {
        PrepareAsync(message, useAsync: false).GetAwaiter().GetResult();
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex
    )
    {
        await PrepareAsync(message, useAsync: true).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static async ValueTask PrepareAsync(PipelineMessage message, bool useAsync)
    {
        var request = message.Request;
        var endpoint = request.Uri;
        if (
            endpoint is null
            || !string.Equals(endpoint.Host, "api.deepseek.com", StringComparison.OrdinalIgnoreCase)
            || !endpoint.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal)
            || request.Content is null
        )
            return;
        using var buffer = new MemoryStream();
        if (useAsync)
            await request
                .Content.WriteToAsync(buffer, message.CancellationToken)
                .ConfigureAwait(false);
        else
            request.Content.WriteTo(buffer, message.CancellationToken);
        var body = JsonNode.Parse(buffer.ToArray())!.AsObject();
        if (body["tools"] is not JsonArray { Count: > 0 })
            return;
        var choice = body["tool_choice"];
        var forced =
            choice is JsonObject
            || (
                choice is JsonValue value
                && value.TryGetValue<string>(out var mode)
                && mode is "required" or "none"
            );
        if (!forced)
            return;
        if (body["thinking"] is not null)
        {
            if (body["thinking"]?["type"]?.GetValue<string>() != "disabled")
                throw new NotSupportedException(
                    "DeepSeek forced tool selection requires disabled thinking."
                );
            return;
        }
        body["thinking"] = new JsonObject { ["type"] = "disabled" };
        request.Content = BinaryContent.Create(BinaryData.FromString(body.ToJsonString()));
    }
}
