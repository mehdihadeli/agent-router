using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using AgentRouter.Storage;

namespace AgentRouter;

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
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter())
        );
        builder.Services.AddOpenApi(
            "management-v1",
            options =>
                options.AddDocumentTransformer(
                    (document, context, cancellationToken) =>
                    {
                        document.Info.Title = "Agent Router Management";
                        document.Info.Version = "v1";
                        document.Info.Description =
                            "Every operation requires the separate management credential in Authorization: Bearer. Inference credentials are not accepted.";
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
                if (
                    !CryptographicOperations.FixedTimeEquals(
                        expected,
                        SHA256.HashData(Encoding.UTF8.GetBytes(supplied))
                    )
                )
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
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
        app.MapOpenApi("/management/v1/openapi/{documentName}.json");
        return app;
    }
}
