using System.Text.Json;
using AgentRouter.Models.Anthropic;
using Xunit;

namespace AgentRouter.Tests;

public class AnthropicContentBlockConverterTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

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
        var json = "{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"call_1\",\"content\":\"ok\"}]}";

        var msg = JsonSerializer.Deserialize<Message>(json, WebJson);
        Assert.NotNull(msg);
        var tr = Assert.IsType<ToolResultBlock>(msg!.Content.Blocks![0]);
        Assert.Equal("call_1", tr.ToolUseId);
        Assert.Equal("ok", tr.Content);
    }
}

