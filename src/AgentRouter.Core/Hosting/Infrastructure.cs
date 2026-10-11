using AgentRouter.ErrorHandling;
using AgentRouter.Shared.AIProviders;
using AgentRouter.Shared.Configuration;

namespace AgentRouter.Extensions;

public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddInfrastructure(
        this IHostApplicationBuilder builder,
        ClaudeGatewayOptions? runtimeOptions = null
    )
    {
        builder.Services.AddExceptionHandler<AnthropicExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.AddServiceDefaults();

        builder.AddAIProviderChatClient(runtimeOptions);

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
