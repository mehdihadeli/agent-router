using ClaudeCodeGateway.Models.Anthropic;
using Common.ErrorHandling;
using Common.ErrorHandling.Exceptions;

namespace ClaudeCodeGateway.Validation;

/// <summary>
/// Validates an incoming Anthropic Messages API request before processing.
/// Each rule throws the appropriate <see cref="CustomException"/> subclass directly.
/// The global <see cref="AnthropicExceptionHandler"/>
/// catches them and produces Anthropic-compatible error responses automatically.
/// </summary>
public static class AnthropicRequestValidator
{
    /// <summary>
    /// Validates the <paramref name="request"/> and throws on the first violated rule.
    /// </summary>
    public static void ThrowIfInvalid(AnthropicRequest? request)
    {
        if (request is null)
        {
            throw new BadRequestException("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ValidationException("'model' is required and must be a non-empty string.");
        }

        if (request.MaxTokens == 0)
        {
            throw new ValidationException("'max_tokens' is required and must be greater than 0.");
        }

        if (request.Messages is not { Count: > 0 })
        {
            throw new ValidationException("'messages' is required and must be a non-empty array.");
        }

        if (!string.Equals(request.Messages[0].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("The first message must have role 'user'.");
        }

        for (var i = 0; i < request.Messages.Count; i++)
        {
            var msg = request.Messages[i];

            if (string.IsNullOrWhiteSpace(msg.Role))
            {
                throw new ValidationException($"messages[{i}].role is required.");
            }

            var role = msg.Role.ToLowerInvariant();
            if (role is not ("user" or "assistant"))
            {
                throw new ValidationException(
                    $"messages[{i}].role must be 'user' or 'assistant', got '{msg.Role}'."
                );
            }
        }

        if (request.Tools is { Count: > 0 })
        {
            for (var i = 0; i < request.Tools.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(request.Tools[i].Name))
                {
                    throw new ValidationException(
                        $"tools[{i}].name is required and must be a non-empty string."
                    );
                }
            }
        }
    }
}
