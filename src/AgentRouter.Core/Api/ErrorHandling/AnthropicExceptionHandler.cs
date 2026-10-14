using System.Text.Json;
using AgentRouter.Core.Api.ErrorHandling.Exceptions;
using AgentRouter.Core.Api.Models.Anthropic;
using Microsoft.AspNetCore.Diagnostics;

namespace AgentRouter.Core.Api.ErrorHandling;

/// <summary>
/// Global exception handler that catches all unhandled exceptions and maps them
/// to the Anthropic API error format so Claude Code can parse error responses.
///
/// Custom exceptions (<see cref="CustomException"/> hierarchy) are the primary path.
/// Standard .NET exceptions (from upstream providers / infrastructure) are the fallback.
/// </summary>
public class AnthropicExceptionHandler(ILogger<AnthropicExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path
        );

        var (statusCode, errorResponse) = MapException(exception);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            httpContext.Request.Path.StartsWithSegments("/v1/responses")
                ? (object)
                    new
                    {
                        error = new
                        {
                            message = errorResponse.Error.Message,
                            type = errorResponse.Error.Type,
                            param = (string?)null,
                            code = (string?)null,
                        },
                    }
                : errorResponse,
            JsonOptions,
            cancellationToken
        );

        return true;
    }

    /// <summary>
    /// Maps exceptions to Anthropic error responses with appropriate HTTP status codes.
    ///
    /// Priority:
    ///   1. <see cref="CustomException"/> hierarchy — uses <see cref="CustomException.StatusCode"/>
    ///      to pick the right Anthropic error type.
    ///   2. Standard .NET exceptions from upstream providers / infrastructure.
    /// </summary>
    internal static (int StatusCode, AnthropicErrorResponse Response) MapException(
        Exception exception
    )
    {
        return exception switch
        {
            // ── Custom exception hierarchy (application-level errors) ─────────

            // 400 — validation / bad request (ValidationException extends BadRequestException)
            ValidationException ex => (
                StatusCodes.Status400BadRequest,
                AnthropicErrorResponse.InvalidRequest(ex.Message)
            ),

            BadRequestException ex => (
                StatusCodes.Status400BadRequest,
                AnthropicErrorResponse.InvalidRequest(ex.Message)
            ),

            // 401 — authentication
            UnAuthorizedException ex => (
                StatusCodes.Status401Unauthorized,
                AnthropicErrorResponse.Authentication(ex.Message)
            ),

            // 403 — permission
            ForbiddenException ex => (
                StatusCodes.Status403Forbidden,
                AnthropicErrorResponse.Permission(ex.Message)
            ),

            // 404 — not found
            NotFoundException ex => (
                StatusCodes.Status404NotFound,
                AnthropicErrorResponse.NotFound(ex.Message)
            ),

            // 409 — conflict
            ConflictException ex => (
                StatusCodes.Status409Conflict,
                AnthropicErrorResponse.Api(ex.Message)
            ),

            // Catch-all for any future CustomException subclass
            CustomException ex => (ex.StatusCode, AnthropicErrorResponse.Api(ex.Message)),

            // ── Standard .NET exceptions (infrastructure / upstream errors) ───

            // JSON deserialization failures (malformed body)
            BadHttpRequestException ex => (
                ex.StatusCode,
                AnthropicErrorResponse.InvalidRequest(
                    ex.StatusCode switch
                    {
                        StatusCodes.Status413PayloadTooLarge => "Request body is too large.",
                        StatusCodes.Status415UnsupportedMediaType =>
                            "Content-Type must be application/json.",
                        _ => "Invalid request body.",
                    }
                )
            ),

            JsonException => (
                StatusCodes.Status400BadRequest,
                AnthropicErrorResponse.InvalidRequest("Invalid JSON in request body.")
            ),

            // Request cancelled (client disconnected)
            OperationCanceledException => (
                499,
                AnthropicErrorResponse.Api("Request was cancelled.")
            ),

            // Upstream provider errors — map by HTTP status code
            Polly.CircuitBreaker.BrokenCircuitException
            or Polly.RateLimiting.RateLimiterRejectedException => MapUpstreamStatus(503),
            HttpRequestException ex => MapUpstreamStatus((int?)ex.StatusCode),
            System.ClientModel.ClientResultException ex => MapUpstreamStatus(ex.Status),
            Azure.RequestFailedException ex => MapUpstreamStatus(ex.Status),
            Anthropic.Exceptions.AnthropicApiException ex => MapUpstreamStatus((int)ex.StatusCode),
            Anthropic.Exceptions.AnthropicException => MapUpstreamStatus(null),

            // Timeout
            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                AnthropicErrorResponse.Overloaded("Upstream provider timed out.")
            ),

            // Everything else → 500 api_error
            _ => (
                StatusCodes.Status500InternalServerError,
                AnthropicErrorResponse.Api("An unexpected error occurred.")
            ),
        };
    }

    private static (int StatusCode, AnthropicErrorResponse Response) MapUpstreamStatus(int? status)
    {
        return status switch
        {
            400 or 422 => (
                StatusCodes.Status400BadRequest,
                AnthropicErrorResponse.InvalidRequest("Upstream rejected the request.")
            ),
            401 => (
                StatusCodes.Status401Unauthorized,
                AnthropicErrorResponse.Authentication("Upstream authentication failed.")
            ),
            403 => (
                StatusCodes.Status403Forbidden,
                AnthropicErrorResponse.Permission("Upstream permission denied.")
            ),
            404 => (
                StatusCodes.Status404NotFound,
                AnthropicErrorResponse.NotFound("Upstream resource not found.")
            ),
            413 => (
                StatusCodes.Status413PayloadTooLarge,
                AnthropicErrorResponse.InvalidRequest("Upstream request is too large.")
            ),
            429 => (
                StatusCodes.Status429TooManyRequests,
                AnthropicErrorResponse.RateLimit("Upstream rate limit exceeded.")
            ),
            503 or 529 => (529, AnthropicErrorResponse.Overloaded("Upstream service unavailable.")),
            408 or 504 => (
                StatusCodes.Status504GatewayTimeout,
                AnthropicErrorResponse.Overloaded("Upstream provider timed out.")
            ),
            _ => (
                StatusCodes.Status502BadGateway,
                AnthropicErrorResponse.Overloaded("Upstream provider error.")
            ),
        };
    }
}
