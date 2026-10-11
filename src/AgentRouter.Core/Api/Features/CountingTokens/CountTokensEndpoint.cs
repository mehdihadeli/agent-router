using AgentRouter.Models.Anthropic;

namespace AgentRouter.Features.CountingTokens;

public static class CountTokensEndpoint
{
    public static IEndpointRouteBuilder MapCountTokens(this IEndpointRouteBuilder builder)
    {
        builder.MapPost(
            "/v1/messages/count_tokens",
            (AnthropicRequest anthropicRequest) =>
            {
                // TODO: Implement actual token counting logic
                var tokenCount = 123; // Placeholder
                return Results.Ok(new { tokens = tokenCount });
            }
        );

        return builder;
    }
}
