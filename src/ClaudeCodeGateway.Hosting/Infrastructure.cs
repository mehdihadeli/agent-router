using ClaudeCodeGateway.ErrorHandling;
using ClaudeCodeGateway.Shared.AIProviders;

namespace ClaudeCodeGateway.Extensions;

public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.Services.AddExceptionHandler<AnthropicExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.AddServiceDefaults();

        builder.AddAIProviderChatClient();

        return builder;
    }

    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        // Global exception handler – returns all errors in Anthropic API format.
        app.UseExceptionHandler();
        app.MapDefaultEndpoints();

        return app;
    }
}
