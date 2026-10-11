using System.Net;
using System.Text.Json;
using AgentRouter.ErrorHandling.Exceptions;
using AgentRouter.Models.Anthropic;
using Microsoft.AspNetCore.Diagnostics;

namespace AgentRouter.ErrorHandling;

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
            errorResponse,
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
            JsonException ex => (
                StatusCodes.Status400BadRequest,
                AnthropicErrorResponse.InvalidRequest($"Invalid JSON in request body: {ex.Message}")
            ),

            // Request cancelled (client disconnected)
            OperationCanceledException => (
                499,
                AnthropicErrorResponse.Api("Request was cancelled.")
            ),

            // Upstream provider errors — map by HTTP status code
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } ex => (
                StatusCodes.Status401Unauthorized,
                AnthropicErrorResponse.Authentication(
                    $"Upstream authentication failed: {ex.Message}"
                )
            ),

            HttpRequestException { StatusCode: HttpStatusCode.Forbidden } ex => (
                StatusCodes.Status403Forbidden,
                AnthropicErrorResponse.Permission($"Upstream permission denied: {ex.Message}")
            ),

            HttpRequestException { StatusCode: HttpStatusCode.NotFound } ex => (
                StatusCodes.Status404NotFound,
                AnthropicErrorResponse.NotFound($"Upstream resource not found: {ex.Message}")
            ),

            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ex => (
                StatusCodes.Status429TooManyRequests,
                AnthropicErrorResponse.RateLimit($"Upstream rate limit exceeded: {ex.Message}")
            ),

            HttpRequestException { StatusCode: HttpStatusCode.ServiceUnavailable } ex => (
                529,
                AnthropicErrorResponse.Overloaded($"Upstream service unavailable: {ex.Message}")
            ),

            HttpRequestException ex => (
                StatusCodes.Status502BadGateway,
                AnthropicErrorResponse.Overloaded($"Upstream provider error: {ex.Message}")
            ),

            // Timeout
            TimeoutException ex => (
                StatusCodes.Status504GatewayTimeout,
                AnthropicErrorResponse.Overloaded($"Upstream provider timed out: {ex.Message}")
            ),

            // Everything else → 500 api_error
            _ => (
                StatusCodes.Status500InternalServerError,
                AnthropicErrorResponse.Api($"An unexpected error occurred: {exception.Message}")
            ),
        };
    }
}
