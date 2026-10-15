using AgentRouter.Core.Api.ErrorHandling;
using AgentRouter.Core.Providers;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Sqlite;
using AgentRouter.ServiceDefaults;

namespace AgentRouter.Core.Hosting;

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

        if (builder.Configuration.GetValue<bool>("Observability:Enabled"))
        {
            var paths = GatewayPaths.Resolve(builder.Configuration["AR_CONFIG_DIR"]);
            builder.Services.AddSingleton(new SqliteConnectionFactory(paths));
            builder.Services.AddSingleton<GatewayRequestHistory>();
            builder.Services.AddHostedService(services =>
                services.GetRequiredService<GatewayRequestHistory>()
            );
        }

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
