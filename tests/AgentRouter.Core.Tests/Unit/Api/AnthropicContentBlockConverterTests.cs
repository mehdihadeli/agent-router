using System.Text.Json;
using AgentRouter.Core.Api.Converters;
using AgentRouter.Core.Api.Models.Anthropic;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Api;

public class AnthropicContentBlockConverterTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(null, null)]
    [InlineData(4294967296L, 17L)]
    public void Convert_Response_PreservesReportedUsageAndUnknowns(long? input, long? output)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "OK"))
        {
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        };
        var result = AnthropicToMEAIConverter.ToAnthropicResponse(response, "native-model");
        Assert.Equal(input, result.Usage.InputTokens);
        Assert.Equal(output, result.Usage.OutputTokens);
        var usage = JsonSerializer.SerializeToElement(result, WebJson).GetProperty("usage");
        Assert.Equal(input.HasValue, usage.TryGetProperty("input_tokens", out _));
        Assert.Equal(output.HasValue, usage.TryGetProperty("output_tokens", out _));
    }

    [Theory]
    [InlineData("any", null)]
    [InlineData("tool", "lookup")]
    public void Convert_Request_PreservesSchemaAndRequiredToolChoice(string choice, string? name)
    {
        var request = JsonSerializer.Deserialize<AnthropicRequest>(
            """
            {"model":"native","max_tokens":20,"messages":[{"role":"user","content":"hello"}],
             "tools":[{"name":"lookup","description":"Find entry","input_schema":{"type":"object","properties":{"key":{"type":"string"}},"required":["key"]}}]}
            """,
            WebJson
        )!;
        request.ToolChoice = JsonSerializer.SerializeToElement(new { type = choice, name });
        var (_, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(request);
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(options.Tools!));
        Assert.False(tool is AIFunction);
        Assert.Equal("lookup", tool.Name);
        Assert.Equal(
            "string",
            tool.JsonSchema.GetProperty("properties")
                .GetProperty("key")
                .GetProperty("type")
                .GetString()
        );
        Assert.Equal("key", tool.JsonSchema.GetProperty("required")[0].GetString());
        Assert.Equal(
            name,
            Assert.IsType<RequiredChatToolMode>(options.ToolMode).RequiredFunctionName
        );
    }

    [Theory]
    [InlineData("end_turn", null)]
    [InlineData("tool_use", null)]
    [InlineData("stop_sequence", "DONE")]
    public void Serialize_Response_UsesAnthropicStopFieldNames(
        string stopReason,
        string? stopSequence
    )
    {
        var response = new AnthropicResponse
        {
            Id = "msg_test",
            ResponseType = "message",
            Role = "assistant",
            Model = "native-model",
            Content = [new ResponseText { Text = "OK" }],
            StopReason = stopReason,
            StopSequence = stopSequence,
            Usage = new Usage { InputTokens = 5, OutputTokens = 3 },
        };
        var json = JsonSerializer.SerializeToElement(response, WebJson);
        Assert.Equal(stopReason, json.GetProperty("stop_reason").GetString());
        Assert.Equal(stopSequence, json.GetProperty("stop_sequence").GetString());
        Assert.False(json.TryGetProperty("stopReason", out _));
        Assert.False(json.TryGetProperty("stopSequence", out _));
        Assert.Equal("message", json.GetProperty("type").GetString());
        Assert.Equal("OK", json.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(5, json.GetProperty("usage").GetProperty("input_tokens").GetInt32());
    }

    [Fact]
    public void Deserialize_MessageContent_ArrayOfObjectsMissingType_TextIsHandled()
    {
        // Claude Code sometimes omits the "type" discriminator for text blocks.
        var json = "{\"role\":\"user\",\"content\":[{\"text\":\"hello\"}]}";

        var msg = JsonSerializer.Deserialize<Message>(json, WebJson);
        Assert.NotNull(msg);
        Assert.NotNull(msg!.Content.Blocks);
        Assert.Single(msg.Content.Blocks!);
        var tb = Assert.IsType<TextBlock>(msg.Content.Blocks![0]);
        Assert.Equal("hello", tb.Text);
    }

    [Fact]
    public void Deserialize_MessageContent_ArrayOfStrings_IsHandled()
    {
        // Claude Code can sometimes emit string blocks in arrays.
        var json = "{\"role\":\"user\",\"content\":[\"hello\",\"world\"]}";

        var msg = JsonSerializer.Deserialize<Message>(json, WebJson);
        Assert.NotNull(msg);
        Assert.Equal(2, msg!.Content.Blocks!.Count);
        Assert.All(msg.Content.Blocks!, b => Assert.IsType<TextBlock>(b));
    }

    [Fact]
    public void Deserialize_ToolResultBlock_MissingType_IsHandled()
    {
        var json =
            "{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"call_1\",\"content\":\"ok\"}]}";

        var msg = JsonSerializer.Deserialize<Message>(json, WebJson);
        Assert.NotNull(msg);
        var tr = Assert.IsType<ToolResultBlock>(msg!.Content.Blocks![0]);
        Assert.Equal("call_1", tr.ToolUseId);
        Assert.Equal("ok", tr.Content);
    }
}
