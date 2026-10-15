using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgentRouter.Core.Contracts.Serialization;
using AgentRouter.Core.Contracts.Stats;
using AgentRouter.Core.Hosting.Browser;
using AgentRouter.Core.Storage;
using AgentRouter.Core.Storage.Sqlite;

namespace AgentRouter.Core.Hosting;

/// <summary>Identity returned only to authenticated management clients.</summary>
public sealed record GatewayServiceIdentity(
    string InstanceId,
    int ProcessId,
    long ProcessStartTicks,
    string Version
);

public static class GatewayManagementApplication
{
    public static WebApplicationBuilder CreateBuilder(
        string token,
        int port,
        GatewayPaths? paths = null
    )
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new ArgumentException(
                "Management token must contain at least 32 characters.",
                nameof(token)
            );
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [HostDefaults.ApplicationKey] = builder.Environment.ApplicationName,
                [HostDefaults.EnvironmentKey] = builder.Environment.EnvironmentName,
                [HostDefaults.ContentRootKey] = builder.Environment.ContentRootPath,
            }
        );
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.WebHost.ConfigureKestrel(options =>
            options.Limits.MaxRequestBodySize = 1024 * 1024
        );
        builder.Logging.ClearProviders();
        builder.Services.AddGatewayStorage(options => options.Directory = paths?.RootDirectory);
        builder.Services.AddSingleton<GatewayConfigurationService>();
        builder.Services.AddSingleton<GatewayBrowserAccess>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<GatewayLifecycleService>();
        builder.Services.AddHostedService(services =>
            services.GetRequiredService<GatewayLifecycleService>()
        );
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            foreach (var converter in GatewayJson.Options.Converters)
                options.SerializerOptions.Converters.Add(converter);
        });
        builder.Services.AddOpenApi(
            "management-v1",
            options =>
                options.AddDocumentTransformer(
                    (document, context, cancellationToken) =>
                    {
                        document.Info.Title = "Agent Router Management";
                        document.Info.Version = "v1";
                        document.Info.Description =
                            "Management requires a separate Bearer credential or a paired browser session. Browser mutations require same-origin and CSRF validation. Inference credentials are not accepted.";
                        return Task.CompletedTask;
                    }
                )
        );
        return builder;
    }

    public static WebApplication Build(
        WebApplicationBuilder builder,
        string token,
        string instanceId
    )
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
            throw new ArgumentException(
                "Management token must contain at least 32 characters.",
                nameof(token)
            );
        if (!Guid.TryParseExact(instanceId, "N", out _))
            throw new ArgumentException(
                "Instance ID must be a GUID in N format.",
                nameof(instanceId)
            );
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'";
                var host = context.Request.Host;
                var origin = context.Request.Headers.Origin.ToString();
                if (
                    !host.Host.Equals("127.0.0.1", StringComparison.Ordinal)
                    || (
                        origin.Length > 0
                        && !origin.Equals($"http://{host}", StringComparison.Ordinal)
                    )
                )
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                var authorization = context.Request.Headers.Authorization.ToString();
                var supplied = authorization.StartsWith(
                    "Bearer ",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? authorization[7..]
                    : "";
                var bearer = CryptographicOperations.FixedTimeEquals(
                    expected,
                    SHA256.HashData(Encoding.UTF8.GetBytes(supplied))
                );
                var browser = context.RequestServices.GetRequiredService<GatewayBrowserAccess>();
                var path = context.Request.Path;
                var browserPath = path.Value?.TrimEnd('/');
                var publicUi =
                    HttpMethods.IsGet(context.Request.Method)
                    && (path == "/" || path.StartsWithSegments("/ui"));
                var pairing =
                    HttpMethods.IsPost(context.Request.Method)
                    && string.Equals(
                        browserPath,
                        "/management/v1/browser/session",
                        StringComparison.OrdinalIgnoreCase
                    );
                if (
                    pairing && (origin != $"http://{host}" || !context.Request.HasJsonContentType())
                )
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (!bearer && !publicUi && !pairing)
                {
                    var session = browser.FindSession(context);
                    if (
                        session is null
                        || string.Equals(
                            browserPath,
                            "/management/v1/browser/pairings",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                    if (
                        !HttpMethods.IsGet(context.Request.Method)
                        && (
                            !GatewayBrowserAccess.Matches(
                                session.CsrfToken,
                                context.Request.Headers["X-AR-CSRF"].ToString()
                            )
                            || origin != $"http://{host}"
                        )
                    )
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }
                }
                await next(context);
            }
        );
        using var process = Process.GetCurrentProcess();
        var identity = new GatewayServiceIdentity(
            instanceId,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            typeof(GatewayManagementApplication).Assembly.GetName().Version?.ToString() ?? "unknown"
        );
        app.MapGet("/management/v1/identity", () => identity)
            .WithSummary("Read authenticated service identity and readiness.");
        app.MapGet(
                "/management/v1/config",
                async (
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) =>
                    GatewayManagementEndpoints.ToResponse(
                        await configuration.LoadAsync(cancellationToken)
                    )
            )
            .WithSummary("Read management configuration without stored credentials.");
        app.MapPost(
                "/management/v1/shutdown",
                (HttpContext context, IHostApplicationLifetime lifetime) =>
                {
                    context.Response.OnCompleted(() =>
                    {
                        lifetime.StopApplication();
                        return Task.CompletedTask;
                    });
                    return Results.Accepted();
                }
            )
            .WithSummary("Gracefully stop this authenticated managed service.");
        app.MapManagementConfiguration();
        app.MapPost(
                "/management/v1/browser/pairings",
                (GatewayBrowserAccess browser) => browser.CreatePairing()
            )
            .WithSummary(
                "Create a single-use browser pairing code; CLI Bearer authorization required."
            );
        app.MapPost(
                "/management/v1/browser/session",
                (
                    HttpContext context,
                    BrowserPairingRequest request,
                    GatewayBrowserAccess browser
                ) => browser.Pair(context, request)
            )
            .Produces<BrowserSessionResponse>()
            .WithSummary("Exchange a CLI pairing code for a short-lived HttpOnly browser session.");
        app.MapGet(
                "/management/v1/browser/session",
                (HttpContext context, GatewayBrowserAccess browser) =>
                    browser.FindSession(context) is { } session
                        ? Results.Ok(session)
                        : Results.Unauthorized()
            )
            .Produces<BrowserSessionResponse>();
        app.MapDelete(
            "/management/v1/browser/session",
            (HttpContext context, GatewayBrowserAccess browser) => browser.Logout(context)
        );
        app.MapGet(
                "/management/v1/requests",
                async (
                    SqliteConnectionFactory connections,
                    int? limit,
                    CancellationToken cancellationToken
                ) =>
                    await GatewayRequestHistory.ReadRecentAsync(
                        connections,
                        limit ?? 100,
                        cancellationToken
                    )
            )
            .WithSummary(
                "Read retained sanitized inference summaries; requires management authorization."
            );
        app.MapGet(
                "/management/v1/observability",
                async (
                    SqliteConnectionFactory connections,
                    int? hours,
                    CancellationToken cancellationToken
                ) =>
                {
                    var range = hours ?? 24;
                    if (range is < 1 or > 720)
                        throw new ArgumentException("Hours must be between 1 and 720.");
                    return await GatewayRequestHistory.SummarizeAsync(
                        connections,
                        TimeSpan.FromHours(range),
                        cancellationToken
                    );
                }
            )
            .Produces<GatewayObservabilitySummary>()
            .WithSummary(
                "Read sanitized request health and usage metrics for a bounded time window."
            );
        app.MapGet(
                "/management/v1/gateway",
                (GatewayLifecycleService lifecycle) => lifecycle.Status
            )
            .WithSummary("Read inference state independently of management readiness.");
        app.MapPost(
                "/management/v1/gateway/start",
                async (GatewayLifecycleService lifecycle, CancellationToken cancellationToken) =>
                    GatewayResult(await lifecycle.StartGatewayAsync(cancellationToken))
            )
            .Produces<GatewayLifecycleResponse>()
            .Produces<GatewayLifecycleResponse>(StatusCodes.Status409Conflict)
            .WithSummary(
                "Start inference using saved configuration; keep management available on failure."
            );
        app.MapPost(
                "/management/v1/gateway/stop",
                async (GatewayLifecycleService lifecycle, CancellationToken cancellationToken) =>
                    Results.Ok(await lifecycle.StopGatewayAsync(cancellationToken))
            )
            .Produces<GatewayLifecycleResponse>()
            .WithSummary("Drain and stop inference without stopping management.");
        app.MapPost(
                "/management/v1/gateway/restart",
                async (GatewayLifecycleService lifecycle, CancellationToken cancellationToken) =>
                    GatewayResult(await lifecycle.RestartGatewayAsync(cancellationToken))
            )
            .Produces<GatewayLifecycleResponse>()
            .Produces<GatewayLifecycleResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Validate saved configuration then restart inference.");
        app.MapOpenApi("/management/v1/openapi/{documentName}.json");
        app.MapGatewayBrowserUi();
        return app;
    }

    private static IResult GatewayResult(GatewayLifecycleResponse response) =>
        response.Error is null
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status409Conflict);
}
