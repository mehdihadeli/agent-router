using System.Text.Json.Serialization;

namespace ClaudeCodeGateway.Models.Anthropic;

/// <summary>
/// Anthropic API error response format.
/// Claude Code expects errors in this exact shape:
/// <code>
/// {
///   "type": "error",
///   "error": {
///     "type": "invalid_request_error",
///     "message": "..."
///   }
/// }
/// </code>
/// See: https://docs.anthropic.com/en/api/errors
/// </summary>
public class AnthropicErrorResponse
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "error";

    [JsonPropertyName("error")]
    public required AnthropicErrorDetail Error { get; init; }

    // ───────────────────────── Factory helpers ─────────────────────────

    /// <summary>400 – the request body is malformed or missing required fields.</summary>
    public static AnthropicErrorResponse InvalidRequest(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.InvalidRequestError,
                Message = message,
            },
        };

    /// <summary>401 – API key is missing or invalid.</summary>
    public static AnthropicErrorResponse Authentication(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.AuthenticationError,
                Message = message,
            },
        };

    /// <summary>403 – the API key does not have permission.</summary>
    public static AnthropicErrorResponse Permission(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.PermissionError,
                Message = message,
            },
        };

    /// <summary>404 – the requested resource was not found.</summary>
    public static AnthropicErrorResponse NotFound(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.NotFoundError,
                Message = message,
            },
        };

    /// <summary>429 – rate limit exceeded.</summary>
    public static AnthropicErrorResponse RateLimit(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.RateLimitError,
                Message = message,
            },
        };

    /// <summary>500 – unexpected internal error.</summary>
    public static AnthropicErrorResponse Api(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.ApiError,
                Message = message,
            },
        };

    /// <summary>529 – the upstream provider is overloaded.</summary>
    public static AnthropicErrorResponse Overloaded(string message) =>
        new()
        {
            Error = new AnthropicErrorDetail
            {
                Type = ErrorTypes.OverloadedError,
                Message = message,
            },
        };
}

public class AnthropicErrorDetail
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>
/// Well-known Anthropic error type strings.
/// </summary>
public static class ErrorTypes
{
    public const string InvalidRequestError = "invalid_request_error";
    public const string AuthenticationError = "authentication_error";
    public const string PermissionError = "permission_error";
    public const string NotFoundError = "not_found_error";
    public const string RateLimitError = "rate_limit_error";
    public const string ApiError = "api_error";
    public const string OverloadedError = "overloaded_error";
}

