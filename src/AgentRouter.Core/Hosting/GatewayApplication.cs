using AgentRouter.Extensions;
using AgentRouter.Features.CreatingMessage;
using AgentRouter.Shared.Configuration;
using Microsoft.Extensions.Options;
using AgentRouter.Contracts.Defaults;
using System.Security.Cryptography;
using System.Text;
using AgentRouter.Shared.Routes;

namespace AgentRouter;

public static class GatewayApplication
{
    public static WebApplicationBuilder CreateBuilder(
        string[]? args = null,
        string? listenUrl = null
    )
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        builder.Configuration.AddDotEnvFile(optional: true);
        builder.Configuration.AddClaudeSettings();
        builder.Configuration.AddAgentRouterConfig();

        var document = GatewayRuntimeConfiguration.Load();
        var runtimeOptions = document is null ? null : GatewayRuntimeConfiguration.Project(document, builder.Configuration);
        if (document is not null)
        {
            builder.Configuration["GatewayAuthToken"] = document.Server.AuthToken;
        }

        listenUrl ??= builder.Configuration["urls"] ?? $"http://{Environment.GetEnvironmentVariable(GatewayDefaults.BindHostEnvironmentVariable) ?? GatewayDefaults.LoopbackHost}:{document?.Server.ProxyPort ?? 4000}";
        if (!string.IsNullOrWhiteSpace(listenUrl))
        {
            builder.WebHost.UseUrls(listenUrl);
        }

        builder.AddInfrastructure(runtimeOptions);
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.UseInfrastructure();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Claude-Code-Gateway"] = "1";
            await next(context);
        });

        var token = builder.Configuration["GatewayAuthToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/v1"))
                {
                    var provided = context.Request.Headers["x-api-key"].ToString();
                    var authorization = context.Request.Headers.Authorization.ToString();
                    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        provided = authorization[7..];
                    }
                    if (!CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { type = "error", error = new { type = "authentication_error", message = "Invalid gateway API key." } });
                        return;
                    }
                }
                await next(context);
            });
        }

        var options = app.Services.GetRequiredService<IOptions<ClaudeGatewayOptions>>();
        if (
            builder.Configuration.GetValue<bool>("ApplyClaudeSettings")
            && !string.IsNullOrWhiteSpace(options.Value.GatewayBaseAddress)
        )
        {
            ClaudeCodeSettingsWriter.EnsureClaudeCodeSettings(options.Value, token);
        }

        app.MapGet("/", () => "`Agent Router` is up and running!");
        app.MapCreatingMessage();
        app.MapGet("/v1/models", () => new
        {
            data = options.Value.Providers.SelectMany(provider => provider.Models.Select(model => new
            {
                id = RouteModelFactory.GetProviderModelName(provider, model),
                type = "model",
                display_name = model,
                created_at = "1970-01-01T00:00:00Z",
            })),
            has_more = false,
        });
        if (!app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
        }

        return app;
    }
}
