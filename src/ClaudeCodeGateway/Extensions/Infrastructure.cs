using Common.AIProviders;
using Common.ErrorHandling;
using Common.Extensions;

namespace ClaudeCodeGateway.Extensions;

public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.Services.AddExceptionHandler<AnthropicExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.AddCustomOpenTelemetry();

        builder.AddAIProviderChatClient();

        return builder;
    }

    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        // Global exception handler – returns all errors in Anthropic API format.
        app.UseExceptionHandler();
        app.MapHealthCheckEndpoints();

        return app;
    }
}
