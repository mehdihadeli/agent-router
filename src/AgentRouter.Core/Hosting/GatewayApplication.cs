using System.Security.Cryptography;
using System.Text;
using AgentRouter.Core.Api.Features.CreatingMessage;
using AgentRouter.Core.Api.Features.CreatingResponse;
using AgentRouter.Core.Contracts.Configuration;
using AgentRouter.Core.Contracts.Defaults;
using AgentRouter.Core.Routes;
using Microsoft.Extensions.Options;

namespace AgentRouter.Core.Hosting;

public static class GatewayApplication
{
    public static WebApplicationBuilder CreateBuilder(
        string[]? args = null,
        string? listenUrl = null,
        GatewayConfigDocument? document = null
    ) => CreateBuilderCore(args, listenUrl, document, null);

    public static WebApplicationBuilder CreateSettingsBuilder(
        GatewaySettings settings,
        string[]? args = null,
        string? listenUrl = null
    ) => CreateBuilderCore(args, listenUrl, null, settings);

    private static WebApplicationBuilder CreateBuilderCore(
        string[]? args,
        string? listenUrl,
        GatewayConfigDocument? document,
        GatewaySettings? settings
    )
    {
        var builder = WebApplication.CreateBuilder(args ?? []);

        builder.Configuration.AddDotEnvFile(optional: true);
        if (settings is null)
        {
            builder.Configuration.AddClaudeSettings();
            builder.Configuration.AddAgentRouterConfig();
        }

        if (settings is not null)
            document = GatewayRuntimeConfiguration.ResolveSettings(settings, builder.Configuration);
        else
            document ??= GatewayRuntimeConfiguration.Load();
        if (document is not null && settings is null)
        {
            document = GatewayRuntimeConfiguration.ApplyOverrides(document, builder.Configuration);
        }
        var runtimeOptions = document is null
            ? null
            : GatewayRuntimeConfiguration.Project(
                document,
                builder.Configuration,
                applyOverrides: settings is null
            );
        if (settings is not null)
        {
            runtimeOptions!.RoutingSettings = settings;
            runtimeOptions.RequestTimeoutSeconds = settings.Execution.DeadlineSeconds;
            runtimeOptions.MaxProviderAttempts = 1;
        }
        if (document is not null)
        {
            builder.Configuration["GatewayAuthToken"] = document.Server.AuthToken;
        }

        listenUrl ??=
            builder.Configuration["urls"]
            ?? $"http://{Environment.GetEnvironmentVariable(GatewayDefaults.BindHostEnvironmentVariable) ?? GatewayDefaults.LoopbackHost}:{document?.Server.ProxyPort ?? 4000}";
        if (!string.IsNullOrWhiteSpace(listenUrl))
        {
            builder.WebHost.UseUrls(listenUrl);
        }

        builder.AddInfrastructure(runtimeOptions);
        return builder;
    }

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        GatewayResilience.Register(builder.Services);
        var app = builder.Build();

        app.UseInfrastructure();

        app.Use(
            async (context, next) =>
            {
                context.Response.Headers["X-Claude-Code-Gateway"] = "1";
                await next(context);
            }
        );

        var token = builder.Configuration["GatewayAuthToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            app.Use(
                async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/v1"))
                    {
                        var provided = context.Request.Headers["x-api-key"].ToString();
                        var authorization = context.Request.Headers.Authorization.ToString();
                        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            provided = authorization[7..];
                        }
                        if (
                            !CryptographicOperations.FixedTimeEquals(
                                expected,
                                SHA256.HashData(Encoding.UTF8.GetBytes(provided))
                            )
                        )
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            await context.Response.WriteAsJsonAsync(
                                new
                                {
                                    type = "error",
                                    error = new
                                    {
                                        type = "authentication_error",
                                        message = "Invalid gateway API key.",
                                    },
                                }
                            );
                            return;
                        }
                    }
                    await next(context);
                }
            );
        }

        app.Use(
            async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/v1"))
                {
                    var limit = GatewayDefaults.MaxInferenceRequestBodyBytes;
                    if (context.Request.ContentLength > limit)
                        throw new BadHttpRequestException(
                            "Request body is too large.",
                            StatusCodes.Status413PayloadTooLarge
                        );
                    if (
                        HttpMethods.IsPost(context.Request.Method)
                        && (
                            (context.Request.Path.Value ?? string.Empty)
                                .TrimEnd('/')
                                .Equals("/v1/messages", StringComparison.OrdinalIgnoreCase)
                            || (context.Request.Path.Value ?? string.Empty)
                                .TrimEnd('/')
                                .Equals("/v1/responses", StringComparison.OrdinalIgnoreCase)
                        )
                        && !context.Request.HasJsonContentType()
                    )
                        throw new BadHttpRequestException(
                            "Content-Type must be application/json.",
                            StatusCodes.Status415UnsupportedMediaType
                        );
                    var originalBody = context.Request.Body;
                    using var limitedBody = new RequestBodyLimitStream(originalBody, limit);
                    context.Request.Body = limitedBody;
                    try
                    {
                        await next(context);
                    }
                    finally
                    {
                        context.Request.Body = originalBody;
                    }
                    return;
                }
                await next(context);
            }
        );

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
        app.MapCreatingResponse();
        app.MapGet(
            "/v1/models",
            () =>
                new
                {
                    data = options.Value.Providers.SelectMany(provider =>
                        provider.Models.Select(model => new
                        {
                            id = RouteModelFactory.GetProviderModelName(provider, model),
                            type = "model",
                            display_name = model,
                            created_at = "1970-01-01T00:00:00Z",
                        })
                    ),
                    has_more = false,
                }
        );
        if (!app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
        }

        return app;
    }
}
