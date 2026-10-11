using System.Text.Json;
using AgentRouter.Converters;
using AgentRouter.Models.Anthropic;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRouter.Tests;

public class AnthropicMEAIConverterTests
{
    [Fact]
    public void ToMEAIChatCompletionRequest_DelegatesToUnderlyingConverter()
    {
        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 64,
            Messages =
            [
                new Message
                {
                    Role = "user",
                    Content = new MessageContent { Text = "Hi" },
                },
            ],
        };

        var (messages, options) = AnthropicMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.NotEmpty(messages);
        Assert.Equal("Hi", messages[0].Text);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("claude-3-5-sonnet", options.ModelId);
        Assert.Equal(64, options.MaxOutputTokens);
    }

    [Fact]
    public void ToAnthropicResponse_ConvertsTextResponse()
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hello")])
        {
            ModelId = "gpt-4o-mini",
        };

        var anthropic = AnthropicMEAIConverter.ToAnthropicResponse(chatResponse, "gpt-4o-mini");

        Assert.Equal("message", anthropic.ResponseType);
        Assert.Equal("assistant", anthropic.Role);
        Assert.Equal("gpt-4o-mini", anthropic.Model);
        Assert.Single(anthropic.Content);
        Assert.IsType<ResponseText>(anthropic.Content[0]);
        Assert.Equal("Hello", ((ResponseText)anthropic.Content[0]).Text);
        Assert.Equal("end_turn", anthropic.StopReason);
    }

    [Theory]
    [InlineData("auto", "\"auto\"")]
    [InlineData("none", "\"none\"")]
    [InlineData("any", "\"required\"")]
    public void ToMEAIToolChoice_MapsStringChoices(string input, string expectedJson)
    {
        var anthropicToolChoice = JsonSerializer.SerializeToElement(input);

        var meaiToolChoice = AnthropicMEAIConverter.ToMEAIToolChoice(anthropicToolChoice);

        Assert.True(meaiToolChoice.HasValue);
        Assert.Equal(expectedJson, meaiToolChoice!.Value.GetRawText());
    }

    [Fact]
    public void ToMEAIToolChoice_MapsToolObjectToFunctionChoice()
    {
        var anthropicToolChoice = JsonSerializer.SerializeToElement(
            new { type = "tool", name = "t1" }
        );

        var meaiToolChoice = AnthropicMEAIConverter.ToMEAIToolChoice(anthropicToolChoice);

        Assert.True(meaiToolChoice.HasValue);

        var raw = meaiToolChoice!.Value.GetRawText();
        Assert.Contains("\"type\":\"function\"", raw);
        Assert.Contains("\"name\":\"t1\"", raw);
    }

    [Fact]
    public void ToMEAIToolChoice_ReturnsNullForNullInput()
    {
        Assert.Null(AnthropicMEAIConverter.ToMEAIToolChoice(null));
    }
}
