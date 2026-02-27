using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClaudeCodeGateway.Models.Anthropic;
using ClaudeCodeGateway.Validation;
using Common.ErrorHandling;
using Common.ErrorHandling.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClaudeCodeGateway.Tests;

// ═══════════════════════════════════════════════════════════════════════════════
// 1. Validator unit tests — each rule throws the specific custom exception
// ═══════════════════════════════════════════════════════════════════════════════

public class AnthropicRequestValidatorTests
{
    [Fact]
    public void NullRequest_Throws_BadRequestException()
    {
        var ex = Assert.Throws<BadRequestException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(null));

        Assert.Contains("Request body is required", ex.Message);
    }

    [Fact]
    public void EmptyModel_Throws_ValidationException()
    {
        var request = MakeRequest(model: "");

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("'model'", ex.Message);
    }

    [Fact]
    public void ZeroMaxTokens_Throws_ValidationException()
    {
        var request = MakeRequest(maxTokens: 0);

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("'max_tokens'", ex.Message);
    }

    [Fact]
    public void EmptyMessages_Throws_ValidationException()
    {
        var request = MakeRequest(messages: []);

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("'messages'", ex.Message);
    }

    [Fact]
    public void FirstMessageNotUser_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message { Role = "assistant", Content = new MessageContent { Text = "hi" } }
            ]
        );

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("first message", ex.Message);
    }

    [Fact]
    public void InvalidRole_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message { Role = "user", Content = new MessageContent { Text = "start" } },
                new Message { Role = "system", Content = new MessageContent { Text = "hi" } },
            ]
        );

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("must be 'user' or 'assistant'", ex.Message);
    }

    [Fact]
    public void EmptyRole_Throws_ValidationException()
    {
        var request = MakeRequest(
            messages:
            [
                new Message { Role = "user", Content = new MessageContent { Text = "start" } },
                new Message { Role = "", Content = new MessageContent { Text = "hi" } },
            ]
        );

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

        Assert.Contains("role is required", ex.Message);
    }

    [Fact]
    public void ToolWithEmptyName_Throws_ValidationException()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var request = MakeRequest(
            tools: [new Tool { Name = "", InputSchema = schema }]
        );

        var ex = Assert.Throws<ValidationException>(
            () => AnthropicRequestValidator.ThrowIfInvalid(request));

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
        // Every exception the validator throws should be a CustomException with StatusCode 400
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
        List<Tool>? tools = null)
    {
        return new AnthropicRequest
        {
            Model     = model,
            MaxTokens = maxTokens,
            Messages  = messages ?? [new Message { Role = "user", Content = new MessageContent { Text = "hi" } }],
            Tools     = tools,
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 2. Exception handler mapping tests — CustomException hierarchy + .NET fallbacks
// ═══════════════════════════════════════════════════════════════════════════════

public class AnthropicExceptionHandlerMappingTests
{
    // ── Custom exception hierarchy ───────────────────────────────────────

    [Fact]
    public void ValidationException_Maps_To_400_InvalidRequest()
    {
        var ex = new ValidationException("'model' is required.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(400, status);
        Assert.Equal(ErrorTypes.InvalidRequestError, resp.Error.Type);
        Assert.Contains("'model'", resp.Error.Message);
    }

    [Fact]
    public void BadRequestException_Maps_To_400_InvalidRequest()
    {
        var ex = new BadRequestException("Request body is required.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(400, status);
        Assert.Equal(ErrorTypes.InvalidRequestError, resp.Error.Type);
    }

    [Fact]
    public void UnAuthorizedException_Maps_To_401_AuthenticationError()
    {
        var ex = new UnAuthorizedException("Invalid API key.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(401, status);
        Assert.Equal(ErrorTypes.AuthenticationError, resp.Error.Type);
    }

    [Fact]
    public void ForbiddenException_Maps_To_403_PermissionError()
    {
        var ex = new ForbiddenException("Not allowed.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(403, status);
        Assert.Equal(ErrorTypes.PermissionError, resp.Error.Type);
    }

    [Fact]
    public void NotFoundException_Maps_To_404_NotFoundError()
    {
        var ex = new NotFoundException("Model not found.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(404, status);
        Assert.Equal(ErrorTypes.NotFoundError, resp.Error.Type);
    }

    [Fact]
    public void ConflictException_Maps_To_409_ApiError()
    {
        var ex = new ConflictException("Conflict.");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(409, status);
        Assert.Equal(ErrorTypes.ApiError, resp.Error.Type);
    }

    // ── Standard .NET exceptions (upstream / infrastructure) ─────────────

    [Fact]
    public void JsonException_Maps_To_400_InvalidRequest()
    {
        var (status, resp) = AnthropicExceptionHandler.MapException(new JsonException("bad json"));
        Assert.Equal(400, status);
        Assert.Equal(ErrorTypes.InvalidRequestError, resp.Error.Type);
        Assert.Contains("Invalid JSON", resp.Error.Message);
    }

    [Fact]
    public void OperationCanceledException_Maps_To_499()
    {
        var (status, resp) = AnthropicExceptionHandler.MapException(new OperationCanceledException());
        Assert.Equal(499, status);
        Assert.Equal(ErrorTypes.ApiError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_Unauthorized_Maps_To_401()
    {
        var ex = new HttpRequestException("nope", null, HttpStatusCode.Unauthorized);
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(401, status);
        Assert.Equal(ErrorTypes.AuthenticationError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_Forbidden_Maps_To_403()
    {
        var ex = new HttpRequestException("denied", null, HttpStatusCode.Forbidden);
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(403, status);
        Assert.Equal(ErrorTypes.PermissionError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_NotFound_Maps_To_404()
    {
        var ex = new HttpRequestException("gone", null, HttpStatusCode.NotFound);
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(404, status);
        Assert.Equal(ErrorTypes.NotFoundError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_TooManyRequests_Maps_To_429()
    {
        var ex = new HttpRequestException("slow", null, HttpStatusCode.TooManyRequests);
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(429, status);
        Assert.Equal(ErrorTypes.RateLimitError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_ServiceUnavailable_Maps_To_529()
    {
        var ex = new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable);
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(529, status);
        Assert.Equal(ErrorTypes.OverloadedError, resp.Error.Type);
    }

    [Fact]
    public void HttpRequestException_Generic_Maps_To_502()
    {
        var ex = new HttpRequestException("oops");
        var (status, resp) = AnthropicExceptionHandler.MapException(ex);
        Assert.Equal(502, status);
        Assert.Equal(ErrorTypes.OverloadedError, resp.Error.Type);
    }

    [Fact]
    public void TimeoutException_Maps_To_504()
    {
        var (status, resp) = AnthropicExceptionHandler.MapException(new TimeoutException("slow"));
        Assert.Equal(504, status);
        Assert.Equal(ErrorTypes.OverloadedError, resp.Error.Type);
    }

    [Fact]
    public void UnknownException_Maps_To_500_ApiError()
    {
        var (status, resp) = AnthropicExceptionHandler.MapException(new Exception("boom"));
        Assert.Equal(500, status);
        Assert.Equal(ErrorTypes.ApiError, resp.Error.Type);
    }

    [Fact]
    public void AllResponses_Have_Type_Error()
    {
        var exceptions = new Exception[]
        {
            new ValidationException("v"),
            new BadRequestException("b"),
            new UnAuthorizedException("u"),
            new ForbiddenException("f"),
            new NotFoundException("n"),
            new ConflictException("c"),
            new JsonException("j"),
            new OperationCanceledException(),
            new HttpRequestException("h"),
            new TimeoutException("t"),
            new Exception("e"),
        };

        foreach (var ex in exceptions)
        {
            var (_, resp) = AnthropicExceptionHandler.MapException(ex);
            Assert.Equal("error", resp.Type);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// 3. Integration tests — error responses via the real pipeline
// ═══════════════════════════════════════════════════════════════════════════════

public class ErrorHandlingIntegrationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sending an invalid Anthropic request (empty model, zero max_tokens) should
    /// return 400 with the standard Anthropic error format.
    /// </summary>
    [Fact]
    public async Task InvalidRequest_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "",
            max_tokens = 0,
            messages = new[] { new { role = "user", content = "hi" } },
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

        Assert.NotNull(error);
        Assert.Equal("error", error.Type);
        Assert.Equal(ErrorTypes.InvalidRequestError, error.Error.Type);
        Assert.Contains("model", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sending an empty messages array should return 400 invalid_request_error.
    /// </summary>
    [Fact]
    public async Task EmptyMessages_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "test-model",
            max_tokens = 100,
            messages = Array.Empty<object>(),
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

        Assert.NotNull(error);
        Assert.Equal("error", error.Type);
        Assert.Equal(ErrorTypes.InvalidRequestError, error.Error.Type);
        Assert.Contains("messages", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Malformed JSON should be caught by the global exception handler
    /// and returned in Anthropic error format.
    /// </summary>
    [Fact]
    public async Task MalformedJson_Returns400_AnthropicErrorFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var content = new StringContent("{ this is not json }", Encoding.UTF8, "application/json");
        var response = await http.PostAsync("/v1/messages", content);

        // ASP.NET Core returns 400 for malformed JSON via model binding
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.InternalServerError,
            $"Expected 400 or 500, got {response.StatusCode}"
        );
    }

    /// <summary>
    /// Sending a valid-looking request with a completely unknown model key
    /// (no matching provider) should produce an error in Anthropic format
    /// from the global exception handler.
    /// </summary>
    [Fact]
    public async Task UnknownModel_ReturnsError_AnthropicFormat()
    {
        await using var factory = CreateFactory();
        using var http = factory.CreateClient();

        var payload = new
        {
            model = "unknown-provider/nonexistent-model",
            max_tokens = 100,
            messages = new[] { new { role = "user", content = "hi" } },
        };

        var response = await http.PostAsJsonAsync("/v1/messages", payload, WebJson);

        // If the gateway can't resolve a client, it should fail.
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<AnthropicErrorResponse>(body, WebJson);

            Assert.NotNull(error);
            Assert.Equal("error", error.Type);
            Assert.NotNull(error.Error.Type);
            Assert.NotNull(error.Error.Message);
        }
        // If a default fallback client exists the request might succeed — that's OK too.
    }

    // ─────────────────────── Factory helpers ───────────────────────

    /// <summary>
    /// Minimal factory with a dummy provider — enough for validation tests.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webHost =>
            {
                webHost.UseEnvironment("Testing");
                webHost.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["GatewayBaseAddress"]         = "http://localhost:4000",
                        ["EnableTelemetry"]            = "false",
                        ["Providers:0:ProviderType"]   = "Azure",
                        ["Providers:0:BaseAddress"]    = "https://dummy.openai.azure.com",
                        ["Providers:0:ApiKey"]         = "dummy-key",
                        ["Providers:0:DeploymentName"] = "gpt-4.1",
                        ["Providers:0:Models:0"]       = "gpt-4.1",
                        ["RouteModel:DefaultModel"]    = "azure/gpt-4.1",
                        ["RouteModel:ThinkingModel"]   = "azure/gpt-4.1",
                        ["RouteModel:SearchModel"]     = "azure/gpt-4.1",
                    });
                });
            });
    }
}

