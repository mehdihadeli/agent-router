using System.Text.Json;
using ClaudeCodeGateway.Converters;
using ClaudeCodeGateway.Models.Anthropic;
using Microsoft.Extensions.AI;
using Xunit;

namespace ClaudeCodeGateway.Tests;

public class AnthropicToMEAIConverterTests
{
    // ═════════════════════════════════════════════════════════════════════
    // Request conversion: AnthropicRequest → MEAI ChatMessage[] + ChatOptions
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void ToMEAIChatCompletionRequest_MapsSystemAndTextMessages()
    {
        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 128,
            System = new SystemPrompt { Single = "You are a helpful assistant." },
            Messages =
            [
                new Message { Role = "user", Content = new MessageContent { Text = "Hello" } },
            ],
        };

        var (messages, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.Equal("claude-3-5-sonnet", options.ModelId);
        Assert.Equal(128, options.MaxOutputTokens);
        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("You are a helpful assistant.", messages[0].Text);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Equal("Hello", messages[1].Text);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_MapsToolsToOptions()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { city = new { type = "string" } },
            required = new[] { "city" },
        });

        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Tools =
            [
                new Models.Anthropic.Tool
                {
                    Name = "get_weather",
                    Description = "Get weather",
                    InputSchema = schema,
                },
            ],
            Messages =
            [
                new Message { Role = "user", Content = new MessageContent { Text = "Weather?" } },
            ],
        };

        var (_, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.NotNull(options.Tools);
        Assert.Single(options.Tools!);
        Assert.Equal(ChatToolMode.Auto, options.ToolMode);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_MapsToolsFromExtra_WhenStronglyTypedIsNull()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { city = new { type = "string" } },
            required = new[] { "city" },
        });

        var toolsJson = JsonSerializer.SerializeToElement(new object[]
        {
            new { name = "get_weather", description = "Get weather", input_schema = schema },
        });

        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Messages =
            [
                new Message { Role = "user", Content = new MessageContent { Text = "Weather?" } },
            ],
            Extra = new Dictionary<string, JsonElement> { ["tools"] = toolsJson },
        };

        var (_, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.NotNull(options.Tools);
        Assert.Single(options.Tools!);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_SplitsToolResultIntoToolRoleMessage()
    {
        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Messages =
            [
                new Message
                {
                    Role = "user",
                    Content = new MessageContent
                    {
                        Blocks =
                        [
                            new TextBlock { Text = "Result: " },
                            new ToolResultBlock { ToolUseId = "call_1", Content = "42" },
                            new TextBlock { Text = "!" },
                        ],
                    },
                },
            ],
        };

        var (messages, _) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        // Should become: user("Result: "), tool(call_1, "42"), user("!")
        Assert.Equal(3, messages.Count);

        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("Result: ", messages[0].Text);

        Assert.Equal(ChatRole.Tool, messages[1].Role);
        var toolResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[1].Contents));
        Assert.Equal("call_1", toolResult.CallId);

        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Equal("!", messages[2].Text);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_MapsToolUseBlocksToFunctionCallContent()
    {
        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Messages =
            [
                new Message
                {
                    Role = "assistant",
                    Content = new MessageContent
                    {
                        Blocks =
                        [
                            new TextBlock { Text = "Calling tool..." },
                            new ToolUseBlock
                            {
                                Id = "call_1",
                                Name = "get_weather",
                                Input = JsonSerializer.SerializeToElement(new { city = "Paris" }),
                            },
                        ],
                    },
                },
            ],
        };

        var (messages, _) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.Single(messages);
        Assert.Equal(ChatRole.Assistant, messages[0].Role);
        Assert.Equal(2, messages[0].Contents.Count);

        var text = Assert.IsType<TextContent>(messages[0].Contents[0]);
        Assert.Equal("Calling tool...", text.Text);

        var fc = Assert.IsType<FunctionCallContent>(messages[0].Contents[1]);
        Assert.Equal("call_1", fc.CallId);
        Assert.Equal("get_weather", fc.Name);
        Assert.NotNull(fc.Arguments);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_PreservesOrder_TextThenToolUseThenToolResultThenText()
    {
        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Messages =
            [
                new Message
                {
                    Role = "assistant",
                    Content = new MessageContent
                    {
                        Blocks =
                        [
                            new TextBlock { Text = "Step1" },
                            new ToolUseBlock
                            {
                                Id = "call_1",
                                Name = "tool1",
                                Input = JsonSerializer.SerializeToElement(new { x = 1 }),
                            },
                        ],
                    },
                },
                new Message
                {
                    Role = "user",
                    Content = new MessageContent
                    {
                        Blocks =
                        [
                            new ToolResultBlock { ToolUseId = "call_1", Content = "ok" },
                            new TextBlock { Text = "Step2" },
                        ],
                    },
                },
            ],
        };

        var (messages, _) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        // 0) assistant with text "Step1" + FunctionCallContent
        // 1) tool with FunctionResultContent
        // 2) user with text "Step2"
        Assert.Equal(3, messages.Count);

        Assert.Equal(ChatRole.Assistant, messages[0].Role);
        Assert.Equal(2, messages[0].Contents.Count);
        Assert.IsType<TextContent>(messages[0].Contents[0]);
        Assert.IsType<FunctionCallContent>(messages[0].Contents[1]);

        Assert.Equal(ChatRole.Tool, messages[1].Role);
        var toolResult = Assert.IsType<FunctionResultContent>(Assert.Single(messages[1].Contents));
        Assert.Equal("call_1", toolResult.CallId);

        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Equal("Step2", messages[2].Text);
    }

    [Fact]
    public void ToMEAIChatCompletionRequest_ToolChoiceNone_SetsToolModeNone()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
        });

        var req = new AnthropicRequest
        {
            Model = "claude-3-5-sonnet",
            MaxTokens = 32,
            Tools =
            [
                new Models.Anthropic.Tool
                {
                    Name = "t1",
                    Description = "tool",
                    InputSchema = schema,
                },
            ],
            ToolChoice = JsonSerializer.SerializeToElement("none"),
            Messages =
            [
                new Message { Role = "user", Content = new MessageContent { Text = "Hi" } },
            ],
        };

        var (_, options) = AnthropicToMEAIConverter.ToMEAIChatCompletionRequest(req);

        Assert.Equal(ChatToolMode.None, options.ToolMode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Response conversion: MEAI ChatResponse → AnthropicResponse
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void ToAnthropicResponse_MapsTextResponse()
    {
        var chatResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Hello world")]);

        var anthropic = AnthropicToMEAIConverter.ToAnthropicResponse(chatResponse, "gpt-4.1");

        Assert.Equal("message", anthropic.ResponseType);
        Assert.Equal("assistant", anthropic.Role);
        Assert.Equal("gpt-4.1", anthropic.Model);
        Assert.Equal("end_turn", anthropic.StopReason);
        Assert.Single(anthropic.Content);
        var text = Assert.IsType<ResponseText>(anthropic.Content[0]);
        Assert.Equal("Hello world", text.Text);
    }

    [Fact]
    public void ToAnthropicResponse_MapsToolCallsToToolUseBlocks()
    {
        var fc = new FunctionCallContent("call_1", "get_weather",
            new Dictionary<string, object?> { ["city"] = "Paris" });
        var msg = new ChatMessage(ChatRole.Assistant, [fc]);
        var chatResponse = new ChatResponse([msg]);

        var anthropic = AnthropicToMEAIConverter.ToAnthropicResponse(chatResponse, "gpt-4.1");

        Assert.Equal("tool_use", anthropic.StopReason);
        var toolUse = Assert.IsType<ResponseToolUse>(Assert.Single(anthropic.Content));
        Assert.Equal("call_1", toolUse.Id);
        Assert.Equal("get_weather", toolUse.Name);
        Assert.Equal(JsonValueKind.Object, toolUse.Input.ValueKind);
    }

    [Fact]
    public void ToAnthropicResponse_InfersToolUseStopReason_WhenToolCallsPresent()
    {
        var contents = new List<AIContent>
        {
            new TextContent("Let me check."),
            new FunctionCallContent("call_1", "t1"),
        };
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);

        var anthropic = AnthropicToMEAIConverter.ToAnthropicResponse(chatResponse, "gpt-4.1");

        Assert.Equal("tool_use", anthropic.StopReason);
        Assert.Equal(2, anthropic.Content.Count);
        Assert.IsType<ResponseText>(anthropic.Content[0]);
        Assert.IsType<ResponseToolUse>(anthropic.Content[1]);
    }

    [Fact]
    public void ToAnthropicResponse_NullChatResponse_ReturnsEmptyContent()
    {
        var anthropic = AnthropicToMEAIConverter.ToAnthropicResponse(null, "gpt-4.1");

        Assert.Equal("end_turn", anthropic.StopReason);
        Assert.Empty(anthropic.Content);
        Assert.Equal((uint)0, anthropic.Usage.InputTokens);
        Assert.Equal((uint)0, anthropic.Usage.OutputTokens);
    }

    // ═════════════════════════════════════════════════════════════════════
    // ConvertToolChoice (internal, kept for AnthropicMEAIConverter facade)
    // ═════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("auto", "\"auto\"")]
    [InlineData("none", "\"none\"")]
    [InlineData("any", "\"required\"")]
    public void ConvertToolChoice_MapsStringChoices(string input, string expectedJson)
    {
        var element = JsonSerializer.SerializeToElement(input);
        var result = AnthropicToMEAIConverter.ConvertToolChoice(element);

        Assert.True(result.HasValue);
        Assert.Equal(expectedJson, result!.Value.GetRawText());
    }

    [Fact]
    public void ConvertToolChoice_MapsToolObjectToFunctionChoice()
    {
        var element = JsonSerializer.SerializeToElement(new { type = "tool", name = "t1" });
        var result = AnthropicToMEAIConverter.ConvertToolChoice(element);

        Assert.True(result.HasValue);
        var raw = result!.Value.GetRawText();
        Assert.Contains("\"type\":\"function\"", raw);
        Assert.Contains("\"name\":\"t1\"", raw);
    }

    [Fact]
    public void ConvertToolChoice_ReturnsNullForNull()
    {
        Assert.Null(AnthropicToMEAIConverter.ConvertToolChoice(null));
    }
}
