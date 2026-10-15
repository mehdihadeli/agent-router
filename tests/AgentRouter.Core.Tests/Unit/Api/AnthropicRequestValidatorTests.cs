using System.Text.Json;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Api.Models.Anthropic;
using AgentRouter.Core.Api.Validation;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Api;

public class AnthropicRequestValidatorTests
{
    [Fact]
    public void NullRequest_Throws_BadRequestException()
    {
        var ex = Assert.Throws<BadRequestException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(null)
        );

        Assert.Contains("Request body is required", ex.Message);
    }

    [Fact]
    public void EmptyModel_Throws_ValidationException()
    {
        var request = MakeRequest(model: "");

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("'model'", ex.Message);
    }

    [Fact]
    public void ZeroMaxTokens_Throws_ValidationException()
    {
        var request = MakeRequest(maxTokens: 0);

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("'max_tokens'", ex.Message);
    }

    [Fact]
    public void EmptyMessages_Throws_ValidationException()
    {
        var request = MakeRequest(messages: []);

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("'messages'", ex.Message);
    }

    [Fact]
    public void FirstMessageNotUser_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message
                {
                    Role = "assistant",
                    Content = new MessageContent { Text = "hi" },
                },
            ]
        );

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("first message", ex.Message);
    }

    [Fact]
    public void InvalidRole_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message
                {
                    Role = "user",
                    Content = new MessageContent { Text = "start" },
                },
                new Message
                {
                    Role = "system",
                    Content = new MessageContent { Text = "hi" },
                },
            ]
        );

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("must be 'user' or 'assistant'", ex.Message);
    }

    [Fact]
    public void EmptyRole_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message
                {
                    Role = "user",
                    Content = new MessageContent { Text = "start" },
                },
                new Message
                {
                    Role = "",
                    Content = new MessageContent { Text = "hi" },
                },
            ]
        );

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("role is required", ex.Message);
    }

    [Fact]
    public void ToolWithEmptyName_Throws_ValidationException()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var request = MakeRequest(tools: [new Tool { Name = "", InputSchema = schema }]);

        var ex = Assert.Throws<ValidationException>(() =>
            AnthropicRequestValidator.ThrowIfInvalid(request)
        );

        Assert.Contains("tools[0].name", ex.Message);
    }

    [Fact]
    public void ValidRequest_DoesNotThrow()
    {
        var request = MakeRequest();
        var exception = Record.Exception(() => AnthropicRequestValidator.ThrowIfInvalid(request));
        Assert.Null(exception);
    }

    [Fact]
    public void AllValidationExceptions_AreBadRequest_StatusCode400()
    {
        var badInputs = new Action[]
        {
            () => AnthropicRequestValidator.ThrowIfInvalid(null),
            () => AnthropicRequestValidator.ThrowIfInvalid(MakeRequest(model: "")),
            () => AnthropicRequestValidator.ThrowIfInvalid(MakeRequest(maxTokens: 0)),
            () => AnthropicRequestValidator.ThrowIfInvalid(MakeRequest(messages: [])),
        };

        foreach (var action in badInputs)
        {
            var ex = Assert.ThrowsAny<CustomException>(action);
            Assert.Equal(400, ex.StatusCode);
        }
    }

    private static AnthropicRequest MakeRequest(
        string model = "gpt-4.1",
        uint maxTokens = 1024,
        List<Message>? messages = null,
        List<Tool>? tools = null
    )
    {
        return new AnthropicRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages =
                messages
                ??
                [
                    new Message
                    {
                        Role = "user",
                        Content = new MessageContent { Text = "hi" },
                    },
                ],
            Tools = tools,
        };
    }
}
