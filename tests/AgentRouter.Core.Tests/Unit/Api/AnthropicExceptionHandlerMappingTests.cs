using System.Net;
using System.Text.Json;
using AgentRouter.Core.Api.ErrorHandling;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Api.Models.Anthropic;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AgentRouter.Core.Tests.Unit.Api;

public class AnthropicExceptionHandlerMappingTests
{
    [Theory]
    [InlineData(413, "Request body is too large.")]
    [InlineData(400, "Invalid request body.")]
    [InlineData(415, "Content-Type must be application/json.")]
    public void BindingFailure_PreservesStatusWithoutPrivateDiagnostics(
        int expected,
        string message
    )
    {
        var (status, response) = AnthropicExceptionHandler.MapException(
            new BadHttpRequestException("private-secret", expected)
        );
        Assert.Equal(expected, status);
        Assert.Equal(ErrorTypes.InvalidRequestError, response.Error.Type);
        Assert.Equal(message, response.Error.Message);
    }

    [Theory]
    [InlineData(400, 400, ErrorTypes.InvalidRequestError)]
    [InlineData(422, 400, ErrorTypes.InvalidRequestError)]
    [InlineData(401, 401, ErrorTypes.AuthenticationError)]
    [InlineData(403, 403, ErrorTypes.PermissionError)]
    [InlineData(404, 404, ErrorTypes.NotFoundError)]
    [InlineData(413, 413, ErrorTypes.InvalidRequestError)]
    [InlineData(429, 429, ErrorTypes.RateLimitError)]
    [InlineData(503, 529, ErrorTypes.OverloadedError)]
    [InlineData(529, 529, ErrorTypes.OverloadedError)]
    [InlineData(408, 504, ErrorTypes.OverloadedError)]
    [InlineData(504, 504, ErrorTypes.OverloadedError)]
    [InlineData(500, 502, ErrorTypes.OverloadedError)]
    public void UpstreamStatus_IsMappedWithoutPrivateDiagnostics(
        int upstream,
        int expected,
        string type
    )
    {
        var exceptions = new Exception[]
        {
            new HttpRequestException(
                "private-secret https://private/provider",
                null,
                (HttpStatusCode)upstream
            ),
            new Azure.RequestFailedException(upstream, "private-secret https://private/provider"),
        };
        foreach (var exception in exceptions)
        {
            var (status, response) = AnthropicExceptionHandler.MapException(exception);
            Assert.Equal(expected, status);
            Assert.Equal(type, response.Error.Type);
            Assert.DoesNotContain(
                "private-secret",
                response.Error.Message,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "https://private",
                response.Error.Message,
                StringComparison.Ordinal
            );
        }
    }

    [Fact]
    public void InfrastructureErrors_UseFixedPublicMessages()
    {
        var cases = new (Exception Exception, string Message)[]
        {
            (new JsonException("private-secret"), "Invalid JSON in request body."),
            (new TimeoutException("private-secret"), "Upstream provider timed out."),
            (new Exception("private-secret"), "An unexpected error occurred."),
            (new HttpRequestException("private-secret"), "Upstream provider error."),
        };
        foreach (var (exception, message) in cases)
            Assert.Equal(
                message,
                AnthropicExceptionHandler.MapException(exception).Response.Error.Message
            );
    }

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
        var (status, resp) = AnthropicExceptionHandler.MapException(
            new OperationCanceledException()
        );
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
